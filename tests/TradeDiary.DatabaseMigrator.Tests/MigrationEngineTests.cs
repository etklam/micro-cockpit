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
    private string _migratorPassword = "";
    private static string Root => FindRoot();
    private static string Migrations => Path.Combine(Root, "platform/postgres/migrations");
    private static string Fingerprint => Path.Combine(Root, "platform/postgres/baseline/legacy-v1-schema.json");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _migratorPassword = $"test-{Guid.NewGuid():N}";
        await Admin(await File.ReadAllTextAsync(Path.Combine(Root, "platform/postgres/roles/001_bootstrap_roles.sql")));
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var format = new NpgsqlCommand("SELECT format('ALTER ROLE trade_diary_migrator PASSWORD %L', $1)", connection);
        format.Parameters.AddWithValue(_migratorPassword);
        await new NpgsqlCommand((string)(await format.ExecuteScalarAsync())!, connection).ExecuteNonQueryAsync();
    }
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task FreshDatabaseIsTransactionalIdempotentAndRoleSeparated()
    {
        await Reset();
        var engine = Engine(Migrations);
        Assert.Equal(0, await engine.RunAsync("migrate"));
        Assert.Equal(13L, await Scalar<long>("SELECT count(*) FROM platform_migrations.schema_history"));
        var applied = await Scalar<DateTime>("SELECT max(applied_at) FROM platform_migrations.schema_history");
        Assert.Equal(0, await engine.RunAsync("migrate"));
        Assert.Equal(applied, await Scalar<DateTime>("SELECT max(applied_at) FROM platform_migrations.schema_history"));
        Assert.Equal(0, await engine.RunAsync("status"));
        await Admin(await File.ReadAllTextAsync(Path.Combine(Root, "platform/postgres/roles/003_finalize_grants.sql")));
        Assert.Equal("trade_diary_migrator", await Scalar<string>("SELECT tableowner FROM pg_tables WHERE schemaname='identity' AND tablename='users'"));
        Assert.True(await Scalar<bool>("SELECT has_table_privilege('identity_service','identity.users','SELECT')"));
        Assert.False(await Scalar<bool>("SELECT has_schema_privilege('identity_service','identity','CREATE')"));
        Assert.False(await Scalar<bool>("SELECT has_table_privilege('identity_service','platform_migrations.schema_history','UPDATE')"));
    }

    [Fact]
    public async Task FailureRollsBackAndHistoryProtectionsRejectDrift()
    {
        await Reset();
        var fixture = CopyMigrations();
        await File.WriteAllTextAsync(Path.Combine(fixture, "0014_failure.sql"), "-- migration-id: 0014\n-- owner: journal-service\n-- description: Failure fixture\n\nCREATE TABLE journal.rollback_probe(id integer);\nSELECT 1 / 0;\n");
        await Assert.ThrowsAnyAsync<Exception>(() => Engine(fixture).RunAsync("migrate"));
        Assert.False(await Scalar<bool>("SELECT to_regclass('journal.rollback_probe') IS NOT NULL"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM platform_migrations.schema_history WHERE migration_id='0014'"));
        File.Delete(Path.Combine(fixture, "0014_failure.sql"));
        await File.AppendAllTextAsync(Path.Combine(fixture, "0001_initial_journal_performance.sql"), "\n-- changed\n");
        await Assert.ThrowsAsync<MigrationException>(() => Engine(fixture).RunAsync("migrate"));
        File.Delete(Path.Combine(fixture, "0001_initial_journal_performance.sql"));
        await Assert.ThrowsAsync<MigrationException>(() => Engine(fixture).RunAsync("migrate"));
        Assert.Equal(13L, await Scalar<long>("SELECT count(*) FROM platform_migrations.schema_history"));
    }

    [Fact]
    public async Task OutOfOrderAndConcurrentRunnersAreSafe()
    {
        await Reset();
        var fixture = Path.Combine(Path.GetTempPath(), $"migration-order-{Guid.NewGuid():N}"); Directory.CreateDirectory(fixture);
        await Simple(fixture, "0001_first.sql", "0001", "CREATE SCHEMA ordered");
        await Simple(fixture, "0003_third.sql", "0003", "CREATE TABLE ordered.third(id integer)");
        await Engine(fixture).RunAsync("migrate");
        await Simple(fixture, "0002_second.sql", "0002", "CREATE TABLE ordered.second(id integer)");
        await Assert.ThrowsAsync<MigrationException>(() => Engine(fixture).RunAsync("migrate"));
        await Reset();
        var results = await Task.WhenAll(Engine(Migrations).RunAsync("migrate"), Engine(Migrations).RunAsync("migrate"));
        Assert.Equal(new[] { 0, 0 }, results);
        Assert.Equal(13L, await Scalar<long>("SELECT count(*) FROM platform_migrations.schema_history"));
    }

    [Fact]
    public async Task BaselineRequiresCompleteFingerprintAndExplicitConfirmation()
    {
        await Reset();
        await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations).RunAsync("baseline", new(true, "backup", Fingerprint)));
        await Admin("CREATE SCHEMA journal");
        await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations).RunAsync("baseline", new(true, "backup", Fingerprint)));
        await Reset(); await ApplyLegacy();
        await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations).RunAsync("migrate"));
        await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations).RunAsync("baseline", new(false, "backup", Fingerprint)));
        Assert.Equal(0, await Engine(Migrations).RunAsync("baseline", new(true, "backup", Fingerprint)));
        Assert.Equal(13L, await Scalar<long>("SELECT count(*) FROM platform_migrations.schema_history WHERE baseline"));
        Assert.Equal(0, await Engine(Migrations).RunAsync("baseline", new(true, "backup", Fingerprint)));
        await Reset(); await ApplyLegacy(); await Admin("CREATE TABLE journal.unexpected_object(id integer)");
        await Assert.ThrowsAsync<MigrationException>(() => Engine(Migrations).RunAsync("baseline", new(true, "backup", Fingerprint)));
    }

    private MigrationEngine Engine(string path) => new(MigratorConnection(), path, "test-release", TimeSpan.FromSeconds(30));
    private string MigratorConnection() { var b = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Username="trade_diary_migrator", Password=_migratorPassword }; return b.ConnectionString; }
    private async Task Reset() => await Admin("DO $$ DECLARE s text; BEGIN FOREACH s IN ARRAY ARRAY['platform_migrations','identity','journal','performance','discipline','reminder','market','market_data_public','price_alert','rotation','stock_research','partner','content','operations','ordered'] LOOP EXECUTE format('DROP SCHEMA IF EXISTS %I CASCADE',s); END LOOP; END $$;");
    private async Task ApplyLegacy() { foreach (var path in Directory.GetFiles(Migrations,"*.sql").Order(StringComparer.Ordinal)) await Admin(await File.ReadAllTextAsync(path)); }
    private async Task Admin(string sql) { await using var c=new NpgsqlConnection(_postgres.GetConnectionString()); await c.OpenAsync(); await new NpgsqlCommand(sql,c){CommandTimeout=120}.ExecuteNonQueryAsync(); }
    private async Task<T> Scalar<T>(string sql) { await using var c=new NpgsqlConnection(_postgres.GetConnectionString()); await c.OpenAsync(); return (T)(await new NpgsqlCommand(sql,c).ExecuteScalarAsync())!; }
    private static string CopyMigrations() { var d=Path.Combine(Path.GetTempPath(),$"migration-fixture-{Guid.NewGuid():N}"); Directory.CreateDirectory(d); foreach(var f in Directory.GetFiles(Migrations,"*.sql")) File.Copy(f,Path.Combine(d,Path.GetFileName(f))); return d; }
    private static Task Simple(string d,string f,string id,string sql)=>File.WriteAllTextAsync(Path.Combine(d,f),$"-- migration-id: {id}\n-- owner: platform-legacy\n-- description: Test {id}\n\n{sql};\n");
    private static string FindRoot() { var d=new DirectoryInfo(AppContext.BaseDirectory); while(d is not null&&!File.Exists(Path.Combine(d.FullName,"TradeDiary.slnx"))) d=d.Parent; return d?.FullName??throw new InvalidOperationException("Repository root not found."); }
}
