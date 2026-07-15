using System.Diagnostics;
using System.Text.Json;
using Npgsql;

namespace TradeDiary.DatabaseMigrator;

public sealed class MigrationEngine(string connectionString, string migrationsPath, string releaseSha, TimeSpan lockTimeout)
{
    private const long AdvisoryLockKey = 5571313704020413778L;
    private static readonly string[] ManagedSchemas =
    [
        "identity", "journal", "performance", "discipline", "reminder", "market", "market_data_public",
        "price_alert", "rotation", "stock_research", "partner", "content", "operations"
    ];

    public async Task<int> RunAsync(string command, BaselineOptions? baseline = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await VerifyDatabaseIdentityAsync(connection, cancellationToken);
        var migrations = MigrationFile.Load(migrationsPath);
        await AcquireLockAsync(connection, cancellationToken);
        try
        {
            return command switch
            {
                "migrate" => await MigrateAsync(connection, migrations, cancellationToken),
                "status" => await StatusAsync(connection, migrations, cancellationToken),
                "baseline" => await BaselineAsync(connection, migrations, baseline ?? throw new MigrationException("Baseline confirmation is required."), cancellationToken),
                _ => throw new MigrationException("Command must be migrate, status, or baseline.")
            };
        }
        finally
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock($1)", connection);
            unlock.Parameters.AddWithValue(AdvisoryLockKey);
            await unlock.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static async Task VerifyDatabaseIdentityAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string requiredRole = "trade_diary_migrator";
        await using var command = new NpgsqlCommand("SELECT current_user, session_user", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var currentUser = reader.GetString(0);
        var sessionUser = reader.GetString(1);
        var unexpected = new[] { currentUser, sessionUser }
            .Where(role => role != requiredRole)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unexpected.Length != 0)
            throw new MigrationException($"Unexpected database role: {string.Join(',', unexpected)}");
    }

    private async Task AcquireLockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed <= lockTimeout)
        {
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", connection);
            command.Parameters.AddWithValue(AdvisoryLockKey);
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false)) return;
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
        throw new MigrationException("Timed out waiting for the PostgreSQL migration advisory lock.");
    }

    private async Task<int> MigrateAsync(NpgsqlConnection connection, IReadOnlyList<MigrationFile> migrations, CancellationToken cancellationToken)
    {
        var historyExists = await HistoryExistsAsync(connection, cancellationToken);
        if (!historyExists && await ManagedSchemaExistsAsync(connection, cancellationToken))
            throw new MigrationException("Existing managed schemas have no migration history. Run the explicit baseline workflow after backup and fingerprint verification.");
        if (!historyExists) await CreateHistoryAsync(connection, cancellationToken);
        var history = await LoadHistoryAsync(connection, cancellationToken);
        ValidateHistory(migrations, history);
        var highest = history.Count == 0 ? "0000" : history.Select(row => row.Id).Max(StringComparer.Ordinal)!;
        foreach (var migration in migrations.Where(item => !history.Any(row => row.Id == item.Id)))
        {
            if (string.CompareOrdinal(migration.Id, highest) < 0)
                throw new MigrationException($"Out-of-order migration detected: {migration.Id}");
            await ApplyAsync(connection, migration, cancellationToken);
        }
        Console.WriteLine("Migration completed successfully.");
        return 0;
    }

    private async Task<int> StatusAsync(NpgsqlConnection connection, IReadOnlyList<MigrationFile> migrations, CancellationToken cancellationToken)
    {
        if (!await HistoryExistsAsync(connection, cancellationToken))
        {
            var legacy = await ManagedSchemaExistsAsync(connection, cancellationToken);
            Console.WriteLine("current: none");
            Console.WriteLine($"baseline-required: {legacy.ToString().ToLowerInvariant()}");
            Console.WriteLine($"pending: {string.Join(',', migrations.Select(item => item.Id))}");
            return legacy ? 2 : 0;
        }
        var history = await LoadHistoryAsync(connection, cancellationToken);
        var missingApplied = history.Any(row => migrations.All(item => item.Id != row.Id));
        var checksumMismatch = history.Any(row => migrations.Any(item => item.Id == row.Id && (item.Filename != row.Filename || item.Checksum != row.Checksum)));
        var highest = history.Count == 0 ? "0000" : history.Select(row => row.Id).Max(StringComparer.Ordinal)!;
        var outOfOrder = migrations.Any(item => history.All(row => row.Id != item.Id) && string.CompareOrdinal(item.Id, highest) < 0);
        var pending = migrations.Where(item => history.All(row => row.Id != item.Id)).Select(item => item.Id).ToArray();
        Console.WriteLine($"current: {(history.Count == 0 ? "none" : highest)}");
        Console.WriteLine($"pending: {(pending.Length == 0 ? "none" : string.Join(',', pending))}");
        Console.WriteLine($"baseline: {history.Any(row => row.Baseline).ToString().ToLowerInvariant()}");
        Console.WriteLine($"checksum-mismatch: {checksumMismatch.ToString().ToLowerInvariant()}");
        Console.WriteLine($"missing-applied-file: {missingApplied.ToString().ToLowerInvariant()}");
        Console.WriteLine($"out-of-order: {outOfOrder.ToString().ToLowerInvariant()}");
        return checksumMismatch || missingApplied || outOfOrder ? 2 : 0;
    }

    private async Task<int> BaselineAsync(NpgsqlConnection connection, IReadOnlyList<MigrationFile> migrations, BaselineOptions options, CancellationToken cancellationToken)
    {
        if (!options.ConfirmExistingDatabase || string.IsNullOrWhiteSpace(options.BackupReference))
            throw new MigrationException("Baseline requires --confirm-existing-database and --backup-confirmed.");
        if (await HistoryExistsAsync(connection, cancellationToken))
        {
            var existing = await LoadHistoryAsync(connection, cancellationToken);
            if (existing.Count != 0)
            {
                ValidateHistory(migrations, existing);
                if (existing.Count == migrations.Count && existing.All(row => row.Baseline))
                {
                    Console.WriteLine("Existing database baseline was already recorded and remains valid.");
                    return 0;
                }
                throw new MigrationException("Baseline requires absent, empty, or an already complete matching baseline history.");
            }
        }
        if (!await ManagedSchemaExistsAsync(connection, cancellationToken))
            throw new MigrationException("Baseline is not permitted for a fresh empty database.");
        await VerifyFingerprintAsync(connection, options.FingerprintPath, cancellationToken);
        await CreateHistoryAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var migration in migrations)
            await InsertHistoryAsync(connection, transaction, migration, 0, true, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Console.WriteLine("Existing database baseline recorded after complete schema fingerprint verification.");
        return 0;
    }

    private static void ValidateHistory(IReadOnlyList<MigrationFile> migrations, IReadOnlyList<HistoryRow> history)
    {
        foreach (var row in history)
        {
            var migration = migrations.SingleOrDefault(item => item.Id == row.Id)
                ?? throw new MigrationException($"Applied migration file is missing: {row.Id}");
            if (migration.Filename != row.Filename || migration.Checksum != row.Checksum)
                throw new MigrationException($"Applied migration checksum or filename changed: {row.Id}");
        }
        var highest = history.Count == 0 ? "0000" : history.Select(row => row.Id).Max(StringComparer.Ordinal)!;
        var outOfOrder = migrations.Any(item => history.All(row => row.Id != item.Id) && string.CompareOrdinal(item.Id, highest) < 0);
        if (outOfOrder) throw new MigrationException("An unapplied migration is ordered below the highest applied version.");
    }

    private async Task ApplyAsync(NpgsqlConnection connection, MigrationFile migration, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(System.Text.Encoding.UTF8.GetString(migration.Bytes), connection, transaction) { CommandTimeout = 300 };
            await command.ExecuteNonQueryAsync(cancellationToken);
            await InsertHistoryAsync(connection, transaction, migration, stopwatch.ElapsedMilliseconds, false, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task InsertHistoryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, MigrationFile migration, long elapsedMs, bool baseline, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO platform_migrations.schema_history
              (migration_id,description,filename,checksum_sha256,applied_by,execution_time_ms,release_sha,baseline)
            VALUES ($1,$2,$3,$4,current_user,$5,$6,$7)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(migration.Id);
        command.Parameters.AddWithValue(migration.Description);
        command.Parameters.AddWithValue(migration.Filename);
        command.Parameters.AddWithValue(migration.Checksum);
        command.Parameters.AddWithValue(elapsedMs);
        command.Parameters.AddWithValue(releaseSha);
        command.Parameters.AddWithValue(baseline);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> HistoryExistsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass('platform_migrations.schema_history') IS NOT NULL", connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> ManagedSchemaExistsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = ANY($1))", connection);
        command.Parameters.AddWithValue(ManagedSchemas);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task CreateHistoryAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE SCHEMA IF NOT EXISTS platform_migrations AUTHORIZATION trade_diary_migrator;
            CREATE TABLE IF NOT EXISTS platform_migrations.schema_history (
              migration_id text PRIMARY KEY,
              description text NOT NULL,
              filename text NOT NULL UNIQUE,
              checksum_sha256 char(64) NOT NULL,
              applied_at timestamptz NOT NULL DEFAULT now(),
              applied_by text NOT NULL,
              execution_time_ms bigint NOT NULL CHECK (execution_time_ms >= 0),
              release_sha text NOT NULL,
              baseline boolean NOT NULL DEFAULT false
            );
            REVOKE ALL ON SCHEMA platform_migrations FROM PUBLIC;
            REVOKE ALL ON platform_migrations.schema_history FROM PUBLIC;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<HistoryRow>> LoadHistoryAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<HistoryRow>();
        await using var command = new NpgsqlCommand("SELECT migration_id,filename,checksum_sha256,baseline FROM platform_migrations.schema_history ORDER BY migration_id", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
        return rows;
    }

    private static async Task VerifyFingerprintAsync(NpgsqlConnection connection, string path, CancellationToken cancellationToken)
    {
        var fingerprint = JsonSerializer.Deserialize<SchemaFingerprint>(await File.ReadAllTextAsync(path, cancellationToken), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new MigrationException("Baseline fingerprint is invalid.");
        foreach (var item in fingerprint.RequiredObjects)
        {
            var parts = item.Split('.', 2);
            if (parts.Length != 2) throw new MigrationException("Baseline fingerprint object name is invalid.");
            await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=$1 AND c.relname=$2)", connection);
            command.Parameters.AddWithValue(parts[0]); command.Parameters.AddWithValue(parts[1]);
            if (!(bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false)) throw new MigrationException($"Baseline fingerprint mismatch: missing object {item}");
        }
        foreach (var item in fingerprint.RequiredColumns)
        {
            await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=$1 AND c.relname=$2 AND a.attname=$3 AND a.attnum>0 AND NOT a.attisdropped)", connection);
            command.Parameters.AddWithValue(item.Schema); command.Parameters.AddWithValue(item.Table); command.Parameters.AddWithValue(item.Column);
            if (!(bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false)) throw new MigrationException($"Baseline fingerprint mismatch: missing column {item.Schema}.{item.Table}.{item.Column}");
        }
        foreach (var sql in fingerprint.RequiredCatalogChecks)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            if (!(bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false)) throw new MigrationException("Baseline fingerprint catalog check failed.");
        }
    }
}

public sealed record BaselineOptions(bool ConfirmExistingDatabase, string BackupReference, string FingerprintPath);
public sealed record SchemaFingerprint(string[] RequiredObjects, FingerprintColumn[] RequiredColumns, string[] RequiredCatalogChecks);
public sealed record FingerprintColumn(string Schema, string Table, string Column);
