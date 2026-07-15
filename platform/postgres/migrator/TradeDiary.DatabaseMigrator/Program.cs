using TradeDiary.DatabaseMigrator;

try
{
    if (args.Length == 0) throw new MigrationException("Usage: db-migrator migrate|status|baseline [options]");
    var command = args[0];
    var options = Parse(args.Skip(1).ToArray());
    var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ?? BuildPostgresConnectionString();
    var migrations = Option(options, "--migrations", Environment.GetEnvironmentVariable("MIGRATIONS_PATH") ?? "/migrations");
    var releaseSha = Environment.GetEnvironmentVariable("RELEASE_SHA") ?? "development";
    var timeout = TimeSpan.FromSeconds(int.Parse(Option(options, "--lock-timeout", "60"), System.Globalization.CultureInfo.InvariantCulture));
    BaselineOptions? baseline = null;
    if (command == "baseline")
        baseline = new(options.ContainsKey("--confirm-existing-database"), Option(options, "--backup-confirmed", ""),
            Option(options, "--fingerprint", Environment.GetEnvironmentVariable("BASELINE_FINGERPRINT_PATH") ?? "/baseline/legacy-v1-schema.json"));
    return await new MigrationEngine(connectionString, migrations, releaseSha, timeout).RunAsync(command, baseline);
}
catch (Exception exception) when (exception is MigrationException or Npgsql.NpgsqlException or IOException or UnauthorizedAccessException or FormatException)
{
    Console.Error.WriteLine($"Database migration failed: {exception.Message}");
    return 1;
}

static Dictionary<string, string?> Parse(string[] values)
{
    var result = new Dictionary<string, string?>(StringComparer.Ordinal);
    for (var index = 0; index < values.Length; index++)
    {
        var key = values[index];
        if (!key.StartsWith("--", StringComparison.Ordinal)) throw new MigrationException($"Unexpected argument: {key}");
        if (key == "--confirm-existing-database") { result[key] = null; continue; }
        if (index + 1 >= values.Length) throw new MigrationException($"Missing value for {key}");
        result[key] = values[++index];
    }
    return result;
}

static string Option(Dictionary<string, string?> values, string key, string fallback) =>
    values.TryGetValue(key, out var value) ? value ?? fallback : fallback;

static string BuildPostgresConnectionString()
{
    var password = Environment.GetEnvironmentVariable("PGPASSWORD") ?? throw new MigrationException("DATABASE_URL or PostgreSQL environment variables are required.");
    return new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = Environment.GetEnvironmentVariable("PGHOST") ?? "postgres",
        Port = int.TryParse(Environment.GetEnvironmentVariable("PGPORT"), out var port) ? port : 5432,
        Database = Environment.GetEnvironmentVariable("PGDATABASE") ?? "trade_diary",
        Username = Environment.GetEnvironmentVariable("PGUSER") ?? "trade_diary_migrator",
        Password = password,
        IncludeErrorDetail = false
    }.ConnectionString;
}
