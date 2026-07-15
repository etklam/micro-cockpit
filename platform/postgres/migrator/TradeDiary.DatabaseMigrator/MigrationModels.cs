using System.Security.Cryptography;
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
        return result;
    }
}

public sealed record HistoryRow(string Id, string Filename, string Checksum, bool Baseline);
public sealed class MigrationException(string message) : Exception(message);
