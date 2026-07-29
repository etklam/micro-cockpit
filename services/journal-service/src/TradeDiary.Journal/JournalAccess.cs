using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

/// <summary>
/// Deep diary journal access: idempotency, ownership, mappers, and validation.
/// HTTP routes stay in Program.cs as a thin adapter over this module.
/// </summary>
static class JournalAccess
{
    internal static bool TryUser(HttpRequest request, out Guid userId) =>
        Guid.TryParse(request.HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId);

    internal static bool TryIdempotencyKey(HttpRequest request, out string? key)
    {
        key = IdempotencyRules.Normalize(request.Headers["Idempotency-Key"].FirstOrDefault());
        return IdempotencyRules.IsValid(key);
    }

    internal static async Task<StoredResult> Idempotent<T>(
        NpgsqlDataSource db,
        Guid userId,
        string operation,
        string? key,
        T payload,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<StoredResult>> execute)
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();
        if (key is null)
        {
            var direct = await execute(connection, tx);
            await tx.CommitAsync();
            return direct;
        }

        var hash = IdempotencyRules.ComputeRequestHash(payload);
        await using var reserve = new NpgsqlCommand("""
            INSERT INTO journal.idempotency_keys(user_id,operation,idempotency_key,request_hash)
            VALUES($1,$2,$3,$4) ON CONFLICT DO NOTHING
            """, connection, tx);
        reserve.Parameters.AddWithValue(userId);
        reserve.Parameters.AddWithValue(operation);
        reserve.Parameters.AddWithValue(key);
        reserve.Parameters.AddWithValue(hash);
        var owner = await reserve.ExecuteNonQueryAsync() == 1;
        if (!owner)
        {
            await using var read = new NpgsqlCommand("""
                SELECT request_hash,status_code,location,response FROM journal.idempotency_keys
                WHERE user_id=$1 AND operation=$2 AND idempotency_key=$3 FOR UPDATE
                """, connection, tx);
            read.Parameters.AddWithValue(userId);
            read.Parameters.AddWithValue(operation);
            read.Parameters.AddWithValue(key);
            await using var reader = await read.ExecuteReaderAsync();
            await reader.ReadAsync();
            if (reader.GetString(0) != hash) return Stored(409, null, new { error = "idempotency_key_reused" });
            return new StoredResult(
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                JsonSerializer.Deserialize<JsonElement>(reader.GetString(3)));
        }

        var result = await execute(connection, tx);
        await using var save = new NpgsqlCommand("""
            UPDATE journal.idempotency_keys SET status_code=$4,location=$5,response=$6::jsonb
            WHERE user_id=$1 AND operation=$2 AND idempotency_key=$3
            """, connection, tx);
        save.Parameters.AddWithValue(userId);
        save.Parameters.AddWithValue(operation);
        save.Parameters.AddWithValue(key);
        save.Parameters.AddWithValue(result.StatusCode);
        save.Parameters.AddWithValue((object?)result.Location ?? DBNull.Value);
        save.Parameters.AddWithValue(result.Body.GetRawText());
        await save.ExecuteNonQueryAsync();

        // The response column is jsonb, which canonicalizes object-key order. Read the
        // just-stored value back before returning so the owner and every replay use the
        // exact same serialized shape, including under concurrent requests.
        await using var stored = new NpgsqlCommand("""
            SELECT status_code,location,response::text FROM journal.idempotency_keys
            WHERE user_id=$1 AND operation=$2 AND idempotency_key=$3
            """, connection, tx);
        stored.Parameters.AddWithValue(userId);
        stored.Parameters.AddWithValue(operation);
        stored.Parameters.AddWithValue(key);
        StoredResult storedResult;
        await using (var storedReader = await stored.ExecuteReaderAsync())
        {
            await storedReader.ReadAsync();
            storedResult = new StoredResult(
                storedReader.GetInt32(0),
                storedReader.IsDBNull(1) ? null : storedReader.GetString(1),
                JsonSerializer.Deserialize<JsonElement>(storedReader.GetString(2)));
        }
        await tx.CommitAsync();
        return storedResult;
    }

    // ponytail: camelCase so PascalCase response records serialize to the same camelCase keys as the anonymous projections they replace.
    private static readonly JsonSerializerOptions StoredJsonOptions = CreateStoredJsonOptions();
    private static JsonSerializerOptions CreateStoredJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    internal static StoredResult Stored(int statusCode, string? location, object body) =>
        new(statusCode, location, JsonSerializer.SerializeToElement(body, StoredJsonOptions));

    internal static IResult WriteResult(HttpContext context, StoredResult result)
    {
        if (result.Location is not null) context.Response.Headers.Location = result.Location;
        // 409 idempotency mismatch becomes a RFC7807 problem; 200/201/404 replays stay byte-stored as-is.
        if (result.StatusCode == 409) return Results.Problem("idempotency_key_reused", statusCode: 409);
        return Results.Json(result.Body, statusCode: result.StatusCode);
    }

}
