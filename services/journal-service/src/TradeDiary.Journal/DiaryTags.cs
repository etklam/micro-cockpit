using Npgsql;

/// <summary>
/// Diary tag normalization and atomic replace.
/// Normalized form: trim, ToLowerInvariant, reject empty/control, max 32 chars, max 10 unique tags.
/// </summary>
static class DiaryTags
{
    internal const int MaxTagLength = 32;
    internal const int MaxTagsPerDiary = 10;

    internal static string? NormalizeAll(IReadOnlyList<string>? input, out IReadOnlyList<string> tags)
    {
        tags = Array.Empty<string>();
        if (input is null || input.Count == 0) return null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (var raw in input)
        {
            if (!TryNormalizeOne(raw, out var tag, out var error)) return error;
            if (seen.Add(tag!)) ordered.Add(tag!);
        }
        if (ordered.Count > MaxTagsPerDiary) return "too_many_tags";
        tags = ordered;
        return null;
    }

    internal static bool TryNormalizeOne(string? raw, out string? tag, out string error)
    {
        tag = null;
        error = "invalid_tag";
        if (raw is null) return false;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.Length > MaxTagLength) return false;
        if (trimmed.Any(char.IsControl)) return false;
        // Letters, numbers, spaces, and a small separator set; Unicode letters/digits allowed.
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '/' or '+' or ' ') continue;
            return false;
        }
        tag = trimmed.ToLowerInvariant();
        if (tag.Length == 0) return false;
        error = "";
        return true;
    }

    internal static async Task ReplaceAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid diaryId, Guid userId, IReadOnlyList<string> tags)
    {
        await using (var clear = new NpgsqlCommand("DELETE FROM journal.diary_tags WHERE diary_id=$1 AND user_id=$2", connection, tx))
        {
            clear.Parameters.AddWithValue(diaryId);
            clear.Parameters.AddWithValue(userId);
            await clear.ExecuteNonQueryAsync();
        }
        foreach (var tag in tags)
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO journal.diary_tags(diary_id, user_id, tag) VALUES($1,$2,$3)", connection, tx);
            insert.Parameters.AddWithValue(diaryId);
            insert.Parameters.AddWithValue(userId);
            insert.Parameters.AddWithValue(tag);
            await insert.ExecuteNonQueryAsync();
        }
    }

    internal static async Task<IReadOnlyList<string>> ReadAsync(NpgsqlDataSource db, Guid diaryId, Guid userId)
    {
        await using var command = db.CreateCommand(
            "SELECT tag FROM journal.diary_tags WHERE diary_id=$1 AND user_id=$2 ORDER BY tag");
        command.Parameters.AddWithValue(diaryId);
        command.Parameters.AddWithValue(userId);
        var tags = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) tags.Add(reader.GetString(0));
        return tags;
    }

    internal static async Task<IReadOnlyList<string>> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid diaryId, Guid userId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT tag FROM journal.diary_tags WHERE diary_id=$1 AND user_id=$2 ORDER BY tag", connection, tx);
        command.Parameters.AddWithValue(diaryId);
        command.Parameters.AddWithValue(userId);
        var tags = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) tags.Add(reader.GetString(0));
        return tags;
    }
}
