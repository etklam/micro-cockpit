using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using Testcontainers.PostgreSql;
using TradeDiary.DatabaseMigrator;

namespace TradeDiary.DatabaseMigrator.Tests;

[CollectionDefinition("database-migrator", DisableParallelization = true)] public sealed class DatabaseMigratorCollection;
[Collection("database-migrator")]
public sealed class MigrationEngineTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine")
        .WithDatabase("trade_diary").WithUsername("trade_diary").WithPassword("test-only-container-password").Build();
    private readonly Dictionary<string, string> _passwords = new(StringComparer.Ordinal);
    private static string Root => FindRoot();
    private static string Migrations => Path.Combine(Root, "platform/postgres/migrations");
    private static string Fingerprint => Path.Combine(Root, "platform/postgres/baseline/legacy-v1-schema.json");
    private static readonly Dictionary<string, (string Schema, string Table)> RuntimeRoles = new(StringComparer.Ordinal)
    {
        ["identity_service"] = ("identity", "users"), ["journal_service"] = ("journal", "diaries"),
        ["performance_service"] = ("performance", "daily_performances"), ["discipline_service"] = ("discipline", "disciplines"),
        ["reminder_service"] = ("reminder", "diary_alerts"), ["market_data_service"] = ("market", "symbols"),
        ["price_alert_service"] = ("price_alert", "alerts"), ["rotation_service"] = ("rotation", "market_rotation_universes"),
        ["stock_research_service"] = ("stock_research", "stocks"), ["partner_service"] = ("partner", "partner_links"),
        ["content_service"] = ("content", "posts"), ["operations_service"] = ("operations", "audit_events")
    };
    private static readonly string[] ManagedSchemas =
    ["identity", "journal", "performance", "discipline", "reminder", "market", "market_data_public", "price_alert", "rotation", "stock_research", "partner", "content", "operations"];

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await Admin(await File.ReadAllTextAsync(Path.Combine(Root, "platform/postgres/roles/001_bootstrap_roles.sql")));
        foreach (var role in RuntimeRoles.Keys.Append("trade_diary_migrator"))
        {
            _passwords[role] = $"test-{Guid.NewGuid():N}";
            await SetPassword(role, _passwords[role]);
        }
    }
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task EveryCommandRequiresCurrentAndSessionMigratorIdentity()
    {
        foreach (var command in new[] { "migrate", "status", "baseline" })
        {
            await Reset();
            var options = command == "baseline" ? new BaselineOptions(true, "backup", Fingerprint) : null;
            var exception = await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations, _postgres.GetConnectionString()).RunAsync(command, options));
            Assert.Contains("trade_diary", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Password", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(await Scalar<bool>("SELECT to_regnamespace('platform_migrations') IS NOT NULL"));
            Assert.False(await Scalar<bool>("SELECT to_regnamespace('journal') IS NOT NULL"));
        }

        await Reset();
        await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations, RoleConnection("identity_service")).RunAsync("migrate"));
        Assert.False(await Scalar<bool>("SELECT to_regnamespace('platform_migrations') IS NOT NULL"));
        Assert.False(await Scalar<bool>("SELECT to_regnamespace('journal') IS NOT NULL"));

        await Reset();
        var setRole = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Options = "-c role=trade_diary_migrator" };
        var setRoleException = await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations, setRole.ConnectionString).RunAsync("migrate"));
        Assert.Contains("trade_diary", setRoleException.Message, StringComparison.Ordinal);
        Assert.False(await Scalar<bool>("SELECT to_regnamespace('platform_migrations') IS NOT NULL"));

        await Reset();
        Assert.Equal(0, await Engine(Migrations).RunAsync("migrate"));
    }

    [Fact]
    public async Task RuntimeManifestIsRequiredAndMustMatchEveryExactSqlByte()
    {
        var changed = CopyMigrations();
        await File.AppendAllTextAsync(Path.Combine(changed, "0001_initial_journal_performance.sql"), "-- changed\n");
        await RejectCatalogForAllCommands(changed);

        var absent = CopyMigrations();
        File.Delete(Path.Combine(absent, "manifest.json"));
        await RejectCatalogForAllCommands(absent);

        var extra = CopyMigrations();
        await Simple(extra, "0016_extra.sql", "0016", "SELECT 16");
        await RejectCatalogForAllCommands(extra);

        var missing = CopyMigrations();
        File.Delete(Path.Combine(missing, "0015_diary_review_ownership.sql"));
        await RejectCatalogForAllCommands(missing);

        var reordered = CopyMigrations();
        var manifestPath = Path.Combine(reordered, "manifest.json");
        var document = JsonSerializer.Deserialize<MigrationManifest>(await File.ReadAllBytesAsync(manifestPath))!;
        await File.WriteAllBytesAsync(manifestPath, JsonSerializer.SerializeToUtf8Bytes(new MigrationManifest(1, document.Migrations.Reverse().ToArray())));
        await RejectCatalogForAllCommands(reordered);
    }

    [Fact]
    public async Task FreshDatabaseIsTransactionalIdempotentAndAllRuntimeRolesAreSeparated()
    {
        await Reset();
        var engine = Engine(Migrations);
        Assert.Equal(0, await engine.RunAsync("migrate"));
        Assert.Equal(15L, await Scalar<long>("SELECT count(*) FROM platform_migrations.schema_history"));
        var applied = await Scalar<DateTime>("SELECT max(applied_at) FROM platform_migrations.schema_history");
        Assert.Equal(0, await engine.RunAsync("migrate"));
        Assert.Equal(applied, await Scalar<DateTime>("SELECT max(applied_at) FROM platform_migrations.schema_history"));
        Assert.Equal(0, await engine.RunAsync("status"));
        await Admin(await File.ReadAllTextAsync(Path.Combine(Root, "platform/postgres/roles/003_finalize_grants.sql")));

        Assert.Equal(ManagedSchemas.Length, await Scalar<int>("SELECT count(*)::int FROM pg_namespace WHERE nspname = ANY($1) AND pg_get_userbyid(nspowner)='trade_diary_migrator'", (object)ManagedSchemas));
        Assert.Equal("trade_diary_migrator", await Scalar<string>("SELECT tableowner FROM pg_tables WHERE schemaname='platform_migrations' AND tablename='schema_history'"));
        foreach (var (role, owned) in RuntimeRoles)
        {
            await using var connection = new NpgsqlConnection(RoleConnection(role));
            await connection.OpenAsync();
            foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
                Assert.True(await HasTablePrivilege(role, $"{owned.Schema}.{owned.Table}", privilege), $"{role} lacks {privilege}");
            var expectedSchemas = ExpectedSchemas(role, owned.Schema);
            foreach (var schema in ManagedSchemas)
                Assert.Equal(expectedSchemas.Contains(schema), await HasSchemaPrivilege(role, schema, "USAGE"));
            await Assert.ThrowsAsync<PostgresException>(() => Execute(connection, $"CREATE TABLE {owned.Schema}.runtime_ddl_probe(id integer)"));
            await Assert.ThrowsAsync<PostgresException>(() => Execute(connection, $"ALTER TABLE {owned.Schema}.{owned.Table} ADD COLUMN runtime_ddl_probe integer"));
            await Assert.ThrowsAsync<PostgresException>(() => Execute(connection, "SELECT count(*) FROM platform_migrations.schema_history"));
            await Assert.ThrowsAsync<PostgresException>(() => Execute(connection, "SET ROLE trade_diary_migrator"));
        }
        Assert.False(await HasTablePrivilege("price_alert_service", "market.symbols", "SELECT"));
        Assert.True(await HasTablePrivilege("price_alert_service", "market.published_provider_health_v1", "SELECT"));
        Assert.True(await HasTablePrivilege("rotation_service", "market_data_public.adjusted_daily_bars_v1", "SELECT"));

        await using (var migrator = new NpgsqlConnection(MigratorConnection()))
        {
            await migrator.OpenAsync();
            await Execute(migrator, "CREATE SEQUENCE identity.runtime_sequence_probe");
        }
        await using (var identity = new NpgsqlConnection(RoleConnection("identity_service")))
        {
            await identity.OpenAsync();
            Assert.Equal(1L, (long)(await new NpgsqlCommand("SELECT nextval('identity.runtime_sequence_probe')", identity).ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task DiaryReviewCompositeForeignKeyRejectsInconsistentOwnership()
    {
        await Reset(); await Engine(Migrations).RunAsync("migrate");
        var diaryId = Guid.NewGuid(); var ownerId = Guid.NewGuid(); var otherUserId = Guid.NewGuid();
        await Admin($"INSERT INTO journal.diaries(id,user_id,local_date,title) VALUES ('{diaryId}','{ownerId}','2026-07-16','Ownership')");
        var exception = await Assert.ThrowsAsync<PostgresException>(() => Admin($"INSERT INTO journal.diary_reviews(diary_id,user_id) VALUES ('{diaryId}','{otherUserId}')"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        await Admin($"INSERT INTO journal.diary_reviews(diary_id,user_id) VALUES ('{diaryId}','{ownerId}')");
        Assert.Equal(2, await Scalar<int>("SELECT count(*)::int FROM pg_constraint WHERE conrelid='journal.diary_reviews'::regclass AND contype='f'"));
    }

    [Fact]
    public async Task FailureRollsBackAndHistoryProtectionsRejectDrift()
    {
        await Reset();
        var fixture = CopyMigrations();
        await File.WriteAllTextAsync(Path.Combine(fixture, "0016_failure.sql"), "-- migration-id: 0016\n-- owner: journal-service\n-- description: Failure fixture\n\nCREATE TABLE journal.rollback_probe(id integer);\nSELECT 1 / 0;\n");
        await RefreshManifest(fixture);
        await Assert.ThrowsAnyAsync<Exception>(() => Engine(fixture).RunAsync("migrate"));
        Assert.False(await Scalar<bool>("SELECT to_regclass('journal.rollback_probe') IS NOT NULL"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM platform_migrations.schema_history WHERE migration_id='0016'"));
        File.Delete(Path.Combine(fixture, "0016_failure.sql"));
        await File.AppendAllTextAsync(Path.Combine(fixture, "0001_initial_journal_performance.sql"), "\n-- changed\n");
        await RefreshManifest(fixture);
        await Assert.ThrowsAsync<MigrationException>(() => Engine(fixture).RunAsync("migrate"));
        File.Delete(Path.Combine(fixture, "0001_initial_journal_performance.sql"));
        await RefreshManifest(fixture);
        await Assert.ThrowsAsync<MigrationException>(() => Engine(fixture).RunAsync("migrate"));
        Assert.Equal(15L, await Scalar<long>("SELECT count(*) FROM platform_migrations.schema_history"));
    }

    [Fact]
    public async Task OutOfOrderAndConcurrentRunnersAreSafe()
    {
        await Reset();
        var fixture = Path.Combine(Path.GetTempPath(), $"migration-order-{Guid.NewGuid():N}"); Directory.CreateDirectory(fixture);
        await Simple(fixture, "0001_first.sql", "0001", "CREATE SCHEMA ordered");
        await Simple(fixture, "0002_second.sql", "0002", "CREATE TABLE ordered.second(id integer)");
        await Simple(fixture, "0003_third.sql", "0003", "CREATE TABLE ordered.third(id integer)");
        await RefreshManifest(fixture);
        await Engine(fixture).RunAsync("migrate");
        await Admin("DELETE FROM platform_migrations.schema_history WHERE migration_id='0002'");
        await Assert.ThrowsAsync<MigrationException>(() => Engine(fixture).RunAsync("migrate"));
        await Reset();
        var results = await Task.WhenAll(Engine(Migrations).RunAsync("migrate"), Engine(Migrations).RunAsync("migrate"));
        Assert.Equal(new[] { 0, 0 }, results);
        Assert.Equal(15L, await Scalar<long>("SELECT count(*) FROM platform_migrations.schema_history"));
    }

    [Fact]
    public async Task BaselineThrough0013LeavesNewerMigrationsPendingForRealAdoption()
    {
        await Reset(); await ApplyLegacyThrough("0013");
        await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations).RunAsync("migrate"));
        Assert.Equal(0, await Engine(Migrations).RunAsync("baseline", new(true, "backup", Fingerprint)));
        Assert.Equal(13L, await Scalar<long>("SELECT count(*) FROM platform_migrations.schema_history WHERE baseline"));
        Assert.Equal(13L, await Scalar<long>("SELECT count(*) FROM platform_migrations.schema_history"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM platform_migrations.schema_history WHERE migration_id > '0013'"));
        Assert.False(await Scalar<bool>("SELECT to_regclass('journal.diary_reviews') IS NOT NULL"));
        Assert.Equal(0, await Engine(Migrations).RunAsync("baseline", new(true, "backup", Fingerprint)));
        Assert.Equal(0, await Engine(Migrations).RunAsync("migrate"));
        Assert.True(await Scalar<bool>("SELECT to_regclass('journal.diary_reviews') IS NOT NULL"));
        Assert.False(await Scalar<bool>("SELECT baseline FROM platform_migrations.schema_history WHERE migration_id='0014'"));
        Assert.False(await Scalar<bool>("SELECT baseline FROM platform_migrations.schema_history WHERE migration_id='0015'"));
        Assert.Equal(0, await Engine(Migrations).RunAsync("baseline", new(true, "backup", Fingerprint)));
    }

    [Fact]
    public async Task BaselineRejectsInvalidTargetAndPostBaselineFingerprintObjects()
    {
        await Reset(); await ApplyLegacyThrough("0013");
        var invalidTarget = await FingerprintFixture(text => text.Replace("\"baselineThroughMigrationId\": \"0013\"", "\"baselineThroughMigrationId\": \"9999\"", StringComparison.Ordinal));
        await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations).RunAsync("baseline", new(true, "backup", invalidTarget)));

        var postBaselineObject = await FingerprintFixture(text => text.Replace("\"journal.diaries\"", "\"journal.diary_reviews\", \"journal.diaries\"", StringComparison.Ordinal));
        await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations).RunAsync("baseline", new(true, "backup", postBaselineObject)));
    }

    [Fact]
    public async Task BaselineRejectsMissingLegacyObjectsAndPartialHistory()
    {
        await Reset(); await ApplyLegacyThrough("0013");
        await Admin("DROP TABLE journal.idempotency_keys");
        await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations).RunAsync("baseline", new(true, "backup", Fingerprint)));

        await Reset(); await ApplyLegacyThrough("0013");
        Assert.Equal(0, await Engine(Migrations).RunAsync("baseline", new(true, "backup", Fingerprint)));
        await Admin("DELETE FROM platform_migrations.schema_history WHERE migration_id='0013'");
        await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations).RunAsync("baseline", new(true, "backup", Fingerprint)));
    }

    [Fact]
    public async Task BaselineStillRequiresLegacySchemaConfirmationAndExactCatalog()
    {
        await Reset();
        await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations).RunAsync("baseline", new(true, "backup", Fingerprint)));
        await ApplyLegacyThrough("0013");
        await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations).RunAsync("baseline", new(false, "backup", Fingerprint)));
        await Admin("CREATE TABLE journal.unexpected_object(id integer)");
        await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations).RunAsync("baseline", new(true, "backup", Fingerprint)));
    }

    private async Task RejectCatalogForAllCommands(string path)
    {
        foreach (var command in new[] { "migrate", "status", "baseline" })
        {
            await Reset();
            var options = command == "baseline" ? new BaselineOptions(true, "backup", Fingerprint) : null;
            await Assert.ThrowsAsync<MigrationException>(() => Engine(path).RunAsync(command, options));
            Assert.False(await Scalar<bool>("SELECT to_regnamespace('platform_migrations') IS NOT NULL"));
            Assert.False(await Scalar<bool>("SELECT to_regnamespace('journal') IS NOT NULL"));
        }
    }

    private static HashSet<string> ExpectedSchemas(string role, string owned) => role switch
    {
        "market_data_service" => ["market", "market_data_public"],
        "price_alert_service" => ["price_alert", "market", "market_data_public"],
        "rotation_service" => ["rotation", "market_data_public"],
        _ => [owned]
    };
    private MigrationEngine Engine(string path, string? connection = null) => new(connection ?? MigratorConnection(), path, "test-release", TimeSpan.FromSeconds(30));
    private string MigratorConnection() => RoleConnection("trade_diary_migrator");
    private string RoleConnection(string role) { var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Username = role, Password = _passwords[role] }; return builder.ConnectionString; }
    private async Task SetPassword(string role, string password) { await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync(); await using var format = new NpgsqlCommand("SELECT format('ALTER ROLE %I PASSWORD %L', $1, $2)", connection); format.Parameters.AddWithValue(role); format.Parameters.AddWithValue(password); await new NpgsqlCommand((string)(await format.ExecuteScalarAsync())!, connection).ExecuteNonQueryAsync(); }
    private async Task Reset() => await Admin("DO $$ DECLARE s text; BEGIN FOREACH s IN ARRAY ARRAY['platform_migrations','identity','journal','performance','discipline','reminder','market','market_data_public','price_alert','rotation','stock_research','partner','content','operations','ordered'] LOOP EXECUTE format('DROP SCHEMA IF EXISTS %I CASCADE',s); END LOOP; END $$;");
    private async Task ApplyLegacyThrough(string migrationId)
    {
        await using var connection = new NpgsqlConnection(MigratorConnection()); await connection.OpenAsync();
        foreach (var path in Directory.GetFiles(Migrations, "*.sql").Where(path => string.CompareOrdinal(Path.GetFileName(path)[..4], migrationId) <= 0).Order(StringComparer.Ordinal))
            await Execute(connection, await File.ReadAllTextAsync(path));
    }
    private async Task Admin(string sql) { await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync(); await Execute(connection, sql); }
    private static async Task Execute(NpgsqlConnection connection, string sql) => await new NpgsqlCommand(sql, connection) { CommandTimeout = 120 }.ExecuteNonQueryAsync();
    private async Task<T> Scalar<T>(string sql, params object[] parameters) { await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync(); await using var command = new NpgsqlCommand(sql, connection); foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter); return (T)(await command.ExecuteScalarAsync())!; }
    private Task<bool> HasTablePrivilege(string role, string table, string privilege) => Scalar<bool>("SELECT has_table_privilege($1,$2,$3)", role, table, privilege);
    private Task<bool> HasSchemaPrivilege(string role, string schema, string privilege) => Scalar<bool>("SELECT has_schema_privilege($1,$2,$3)", role, schema, privilege);
    private static string CopyMigrations() { var directory = Path.Combine(Path.GetTempPath(), $"migration-fixture-{Guid.NewGuid():N}"); Directory.CreateDirectory(directory); foreach (var file in Directory.GetFiles(Migrations)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file))); return directory; }
    private static Task Simple(string directory, string filename, string id, string sql) => File.WriteAllTextAsync(Path.Combine(directory, filename), $"-- migration-id: {id}\n-- owner: platform-legacy\n-- description: Test {id}\n\n{sql};\n");
    private static async Task RefreshManifest(string directory) { var entries = Directory.GetFiles(directory, "*.sql").Order(StringComparer.Ordinal).Select(path => new MigrationManifestEntry(Path.GetFileName(path)[..4], Path.GetFileName(path), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))))).ToArray(); await File.WriteAllBytesAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(new MigrationManifest(1, entries))); }
    private static async Task<string> FingerprintFixture(Func<string, string> transform) { var path = Path.Combine(Path.GetTempPath(), $"baseline-fingerprint-{Guid.NewGuid():N}.json"); await File.WriteAllTextAsync(path, transform(await File.ReadAllTextAsync(Fingerprint))); return path; }
    private static string FindRoot() { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TradeDiary.slnx"))) directory = directory.Parent; return directory?.FullName ?? throw new InvalidOperationException("Repository root not found."); }
}
