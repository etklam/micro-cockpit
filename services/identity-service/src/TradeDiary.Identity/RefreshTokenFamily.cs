using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

internal enum RefreshTokenRotationStatus
{
    Rotated,
    Invalid,
    Expired,
    ReusedAndFamilyRevoked,
    AccountInactive
}

internal sealed record RefreshTokenRotationResult(
    RefreshTokenRotationStatus Status,
    AuthTokens? Tokens = null);

internal sealed record RefreshTokenFamilyOptions(
    SecurityKey SigningKey,
    string Issuer,
    string Audience);

// The refresh-token family is deliberately local to Identity. It owns the token
// state machine and the database transaction that changes that state.
internal sealed class RefreshTokenFamily(
    NpgsqlDataSource db,
    RefreshTokenFamilyOptions options)
{
    private const int RefreshTokenBytes = 32;

    public async Task<AuthTokens> IssueAsync(
        AuthUser user,
        Guid familyId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var tokens = await InsertSuccessorAsync(connection, transaction, user, familyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return tokens;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<RefreshTokenRotationResult> RotateAsync(
        string? encodedRefreshToken,
        CancellationToken cancellationToken = default)
    {
        if (!TryHashToken(encodedRefreshToken, out var tokenHash))
            return new(RefreshTokenRotationStatus.Invalid);

        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            RefreshTokenRow? token = null;
            await using (var command = new NpgsqlCommand("""
                SELECT r.id,r.user_id,r.family_id,r.expires_at,r.used_at,r.revoked_at,
                       u.email,u.display_name,u.timezone,u.base_currency,u.role,u.account_type,u.status,u.status_version
                FROM identity.refresh_tokens r
                JOIN identity.users u ON u.id=r.user_id
                WHERE r.token_hash=$1
                FOR UPDATE OF r, u
                """, connection, transaction))
            {
                command.Parameters.AddWithValue(tokenHash);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    token = new RefreshTokenRow(
                        reader.GetGuid(0),
                        reader.GetGuid(2),
                        reader.GetDateTime(3),
                        !reader.IsDBNull(4),
                        !reader.IsDBNull(5),
                        new AuthUser(
                            reader.GetGuid(1),
                            reader.GetString(6),
                            reader.GetString(7),
                            reader.GetString(8),
                            reader.GetString(9),
                            reader.GetString(10),
                            reader.GetString(11),
                            reader.GetString(12),
                            reader.GetInt32(13),
                            "system"));
                }
            }

            if (token is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new(RefreshTokenRotationStatus.Invalid);
            }

            if (!string.Equals(token.User.Status, "active", StringComparison.Ordinal))
            {
                await transaction.CommitAsync(cancellationToken);
                return new(RefreshTokenRotationStatus.AccountInactive);
            }

            // A used or revoked token is a replay signal. Revoke every token in
            // the family in the same transaction before reporting the failure.
            if (token.Used || token.Revoked)
            {
                await RevokeFamilyRowsAsync(connection, transaction, token.FamilyId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(RefreshTokenRotationStatus.ReusedAndFamilyRevoked);
            }

            if (token.ExpiresAt <= DateTime.UtcNow)
            {
                await transaction.CommitAsync(cancellationToken);
                return new(RefreshTokenRotationStatus.Expired);
            }

            await using (var markUsed = new NpgsqlCommand("""
                UPDATE identity.refresh_tokens
                SET used_at=now()
                WHERE id=$1 AND used_at IS NULL AND revoked_at IS NULL
                """, connection, transaction))
            {
                markUsed.Parameters.AddWithValue(token.Id);
                if (await markUsed.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    await RevokeFamilyRowsAsync(connection, transaction, token.FamilyId, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return new(RefreshTokenRotationStatus.ReusedAndFamilyRevoked);
                }
            }

            // Consumption and successor insertion share this transaction. If
            // insertion fails, the old token remains unused after rollback.
            var successor = await InsertSuccessorAsync(
                connection,
                transaction,
                token.User,
                token.FamilyId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(RefreshTokenRotationStatus.Rotated, successor);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task RevokeFamilyAsync(
        string? encodedRefreshToken,
        CancellationToken cancellationToken = default)
    {
        if (!TryHashToken(encodedRefreshToken, out var tokenHash))
            return;

        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            Guid? familyId = null;
            await using (var command = new NpgsqlCommand("""
                SELECT family_id
                FROM identity.refresh_tokens
                WHERE token_hash=$1
                FOR UPDATE
                """, connection, transaction))
            {
                command.Parameters.AddWithValue(tokenHash);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                    familyId = reader.GetGuid(0);
            }

            if (familyId.HasValue)
                await RevokeFamilyRowsAsync(connection, transaction, familyId.Value, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task RevokeFamilyRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid familyId,
        CancellationToken cancellationToken)
    {
        await using var revoke = new NpgsqlCommand("""
            UPDATE identity.refresh_tokens
            SET revoked_at=coalesce(revoked_at,now())
            WHERE family_id=$1
            """, connection, transaction);
        revoke.Parameters.AddWithValue(familyId);
        await revoke.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<AuthTokens> InsertSuccessorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuthUser user,
        Guid familyId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var refreshBytes = RandomNumberGenerator.GetBytes(RefreshTokenBytes);
        await using var command = new NpgsqlCommand("""
            INSERT INTO identity.refresh_tokens (id,user_id,family_id,token_hash,expires_at)
            VALUES ($1,$2,$3,$4,$5)
            """, connection, transaction);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(user.Id);
        command.Parameters.AddWithValue(familyId);
        command.Parameters.AddWithValue(SHA256.HashData(refreshBytes));
        command.Parameters.AddWithValue(now.AddDays(30));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new(
            IdentityAccessTokenIssuer.Create(user, options.SigningKey, options.Issuer, options.Audience, []),
            now.AddMinutes(15),
            Convert.ToBase64String(refreshBytes));
    }

    private static bool TryHashToken(string? encodedRefreshToken, out byte[] tokenHash)
    {
        tokenHash = [];
        if (string.IsNullOrWhiteSpace(encodedRefreshToken))
            return false;

        try
        {
            var refreshBytes = Convert.FromBase64String(encodedRefreshToken);
            if (refreshBytes.Length != RefreshTokenBytes)
                return false;

            tokenHash = SHA256.HashData(refreshBytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record RefreshTokenRow(
        Guid Id,
        Guid FamilyId,
        DateTime ExpiresAt,
        bool Used,
        bool Revoked,
        AuthUser User);
}

internal static class IdentityAccessTokenIssuer
{
    public static string Create(
        AuthUser user,
        SecurityKey signingKey,
        string issuer,
        string audience,
        IReadOnlyCollection<string> scopes)
    {
        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("account_type", user.AccountType),
            new Claim("status_version", user.StatusVersion.ToString()),
            new Claim("timezone", user.Timezone),
            new Claim("base_currency", user.BaseCurrency.Trim())
        }.Concat(scopes.Select(scope => new Claim("scope", scope)));
        var jwt = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            now,
            now.AddMinutes(15),
            new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
