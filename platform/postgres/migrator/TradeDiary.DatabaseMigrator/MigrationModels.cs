using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TradeDiary.DatabaseMigrator;

public sealed record MigrationFile(
    string Id,
    string Description,
    string Owner,
    string Filename,
    string Path,
    string Checksum,
    byte[] Bytes)
{
    private static readonly Regex FilenamePattern = new(@"^(?<id>\d{4})_[a-z0-9_]+\.sql$", RegexOptions.Compiled);
    private static readonly Regex TransactionBreaking = new(
        @"(?im)^\s*(BEGIN|COMMIT|ROLLBACK)\s*;|\\connect\b|\b(?:CREATE|DROP)\s+INDEX\s+CONCURRENTLY\b|^\s*VACUUM\b",
        RegexOptions.Compiled);
    private static readonly Regex Destructive = new(
        @"(?i)\bDROP\s+(?:TABLE|SCHEMA|COLUMN)\b|\bTRUNCATE\b|\bALTER\s+(?:TABLE\s+\S+\s+)?COLUMN\s+\S+\s+TYPE\b|\bRENAME\s+COLUMN\b",
        RegexOptions.Compiled);

    public static IReadOnlyList<MigrationFile> Load(string directory)
    {
        if (!Directory.Exists(directory)) throw new MigrationException("Migration directory does not exist.");
        var result = new List<MigrationFile>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(directory, "*.sql").Order(StringComparer.Ordinal))
        {
            var filename = System.IO.Path.GetFileName(path);
            var match = FilenamePattern.Match(filename);
            if (!match.Success) throw new MigrationException($"Invalid migration filename: {filename}");
            var bytes = File.ReadAllBytes(path);
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            if (lines.Length < 3 || !lines[0].StartsWith("-- migration-id: ", StringComparison.Ordinal) ||
                !lines[1].StartsWith("-- owner: ", StringComparison.Ordinal) ||
                !lines[2].StartsWith("-- description: ", StringComparison.Ordinal))
                throw new MigrationException($"Migration metadata is invalid: {filename}");
            var id = lines[0][17..].Trim();
            if (id != match.Groups["id"].Value) throw new MigrationException($"Migration header ID differs from filename: {filename}");
            if (!ids.Add(id)) throw new MigrationException($"Duplicate migration ID: {id}");
            if (TransactionBreaking.IsMatch(text)) throw new MigrationException($"Migration contains transaction-breaking SQL: {filename}");
            if (Destructive.IsMatch(text)) throw new MigrationException($"Migration contains destructive automatic DDL: {filename}");
            result.Add(new MigrationFile(id, lines[2][16..].Trim(), lines[1][10..].Trim(), filename, path,
                Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes));
        }
        if (result.Count == 0) throw new MigrationException("No migration files were found.");
        var expectedIds = Enumerable.Range(1, result.Count).Select(value => value.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        if (!result.Select(item => item.Id).SequenceEqual(expectedIds, StringComparer.Ordinal))
            throw new MigrationException("Migration IDs must be contiguous and start at 0001.");
        ValidateManifest(directory, result);
        return result;
    }

    private static void ValidateManifest(string directory, IReadOnlyList<MigrationFile> migrations)
    {
        var manifestPath = System.IO.Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath)) throw new MigrationException("Migration manifest is missing.");
        MigrationManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<MigrationManifest>(File.ReadAllBytes(manifestPath))
                ?? throw new MigrationException("Migration manifest is invalid.");
        }
        catch (JsonException)
        {
            throw new MigrationException("Migration manifest is invalid.");
        }
        if (manifest.Format != 1 || manifest.Migrations is null) throw new MigrationException("Migration manifest format is unsupported.");
        if (manifest.Migrations.Count != migrations.Count)
            throw new MigrationException("Migration manifest entry count does not match SQL file count.");
        for (var index = 0; index < migrations.Count; index++)
        {
            var file = migrations[index];
            var entry = manifest.Migrations[index];
            if (entry is null || entry.Id != file.Id || entry.Filename != file.Filename || entry.Sha256 != file.Checksum)
                throw new MigrationException($"Migration manifest does not match exact file bytes and order: {file.Filename}");
        }
    }
}

public sealed record MigrationManifest(
    [property: JsonPropertyName("format")] int Format,
    [property: JsonPropertyName("migrations")] IReadOnlyList<MigrationManifestEntry> Migrations);
public sealed record MigrationManifestEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("sha256")] string Sha256);

public sealed record HistoryRow(string Id, string Filename, string Checksum, bool Baseline);
public sealed class MigrationException(string message) : Exception(message);
