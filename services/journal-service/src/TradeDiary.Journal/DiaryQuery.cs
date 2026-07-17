using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

/// <summary>
/// Server-side diary list: filters, cursor pagination, and parameterized SQL fragments.
/// </summary>
static class DiaryQuery
{
    private const int CursorVersion = 1;
    private const int MaxKeywordLength = 200;
    private const int MaxSymbolLength = 32;
    private const char LikeEscape = '\\';
    private static readonly JsonSerializerOptions CursorJson = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlySet<string> ReviewStatuses = new HashSet<string>(StringComparer.Ordinal)
    {
        "all", "reviewed", "unreviewed"
    };

    internal static string? Validate(
        string? query,
        DateOnly? from,
        DateOnly? to,
        string reviewStatus,
        string? symbol,
        string? tag,
        int limit,
        string? cursor,
        out DiaryListQuery parsed)
    {
        parsed = default!;
        if (from is { } f && to is { } t && f > t) return "invalid_date_range";
        if (!ReviewStatuses.Contains(reviewStatus)) return "invalid_review_status";
        if (limit is < 1 or > 100) return "invalid_limit";

        string? keyword = null;
        if (!string.IsNullOrWhiteSpace(query))
        {
            keyword = query.Trim();
            if (keyword.Length > MaxKeywordLength) return "invalid_query";
        }

        string? normalizedSymbol = null;
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            normalizedSymbol = symbol.Trim().ToUpperInvariant();
            if (normalizedSymbol.Length is 0 or > MaxSymbolLength) return "invalid_symbol";
            if (normalizedSymbol.Any(ch => char.IsControl(ch) || char.IsWhiteSpace(ch))) return "invalid_symbol";
        }

        string? normalizedTag = null;
        if (tag is not null)
        {
            if (!DiaryTags.TryNormalizeOne(tag, out normalizedTag, out var tagError)) return tagError;
        }

        DiaryCursor? parsedCursor = null;
        if (cursor is not null && !TryDecodeCursor(cursor, out parsedCursor)) return "invalid_cursor";

        parsed = new DiaryListQuery(keyword, from, to, reviewStatus, normalizedSymbol, normalizedTag, limit, parsedCursor);
        return null;
    }

    internal static async Task<DiaryPage> ReadAsync(NpgsqlDataSource db, Guid userId, DiaryListQuery query)
    {
        var sql = new StringBuilder("""
            SELECT d.id, d.local_date, d.title, d.content, d.created_at, d.updated_at,
                   coalesce((
                     SELECT array_agg(t.tag ORDER BY t.tag)
                     FROM journal.diary_tags t
                     WHERE t.diary_id = d.id AND t.user_id = d.user_id
                   ), '{}'::text[]) AS tags
            FROM journal.diaries d
            WHERE d.user_id = $1 AND d.deleted_at IS NULL
            """);
        var parameters = new List<object?> { userId };
        var index = 2;

        if (query.From is { } from)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND d.local_date >= ${index}");
            parameters.Add(from);
            index++;
        }
        if (query.To is { } to)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND d.local_date <= ${index}");
            parameters.Add(to);
            index++;
        }
        if (query.Keyword is { } keyword)
        {
            // Case-insensitive literal substring; escape LIKE metacharacters.
            var pattern = "%" + EscapeLike(keyword) + "%";
            sql.Append(CultureInfo.InvariantCulture, $" AND (d.title ILIKE ${index} ESCAPE '{LikeEscape}' OR d.content ILIKE ${index} ESCAPE '{LikeEscape}')");
            parameters.Add(pattern);
            index++;
        }
        if (query.ReviewStatus == "reviewed")
            sql.Append(" AND EXISTS (SELECT 1 FROM journal.diary_reviews r WHERE r.diary_id = d.id AND r.user_id = d.user_id)");
        else if (query.ReviewStatus == "unreviewed")
            sql.Append(" AND NOT EXISTS (SELECT 1 FROM journal.diary_reviews r WHERE r.diary_id = d.id AND r.user_id = d.user_id)");

        if (query.Symbol is { } symbol)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND EXISTS (SELECT 1 FROM journal.transactions tx WHERE tx.diary_id = d.id AND tx.user_id = d.user_id AND tx.deleted_at IS NULL AND tx.symbol = ${index})");
            parameters.Add(symbol);
            index++;
        }
        if (query.Tag is { } tag)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND EXISTS (SELECT 1 FROM journal.diary_tags tg WHERE tg.diary_id = d.id AND tg.user_id = d.user_id AND tg.tag = ${index})");
            parameters.Add(tag);
            index++;
        }
        if (query.Cursor is { } cursor)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND (d.local_date, d.created_at, d.id) < (${index}, ${index + 1}, ${index + 2})");
            parameters.Add(cursor.LocalDate);
            parameters.Add(cursor.CreatedAt);
            parameters.Add(cursor.Id);
            index += 3;
        }

        sql.Append(CultureInfo.InvariantCulture, $" ORDER BY d.local_date DESC, d.created_at DESC, d.id DESC LIMIT ${index}");
        parameters.Add(query.Limit + 1);

        await using var command = db.CreateCommand(sql.ToString());
        foreach (var value in parameters)
        {
            command.Parameters.Add(value switch
            {
                null => new NpgsqlParameter { Value = DBNull.Value },
                DateOnly date => new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Date, Value = date },
                DateTime dt => new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = DateTime.SpecifyKind(dt, DateTimeKind.Utc) },
                Guid id => new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = id },
                int n => new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = n },
                _ => new NpgsqlParameter { Value = value }
            });
        }

        var rows = new List<(DiaryResponse Item, DiaryCursor Cursor)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var item = JournalAccess.ReadDiary(reader);
            rows.Add((item, new DiaryCursor(CursorVersion, item.LocalDate, item.CreatedAt, item.Id)));
        }

        var hasMore = rows.Count > query.Limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return new DiaryPage(rows.Select(row => row.Item).ToList(), hasMore ? EncodeCursor(rows[^1].Cursor) : null);
    }

    private static string EscapeLike(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '%' or '_' or LikeEscape) builder.Append(LikeEscape);
            builder.Append(ch);
        }
        return builder.ToString();
    }

    private static string EncodeCursor(DiaryCursor cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor, CursorJson))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecodeCursor(string value, out DiaryCursor? cursor)
    {
        cursor = null;
        try
        {
            var encoded = value.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            cursor = JsonSerializer.Deserialize<DiaryCursor>(Convert.FromBase64String(encoded), CursorJson);
            return cursor is { Version: CursorVersion } decoded
                && decoded.Id != Guid.Empty
                && decoded.LocalDate != default
                && decoded.CreatedAt != default;
        }
        catch (Exception error) when (error is FormatException or JsonException) { return false; }
    }
}

sealed record DiaryListQuery(
    string? Keyword,
    DateOnly? From,
    DateOnly? To,
    string ReviewStatus,
    string? Symbol,
    string? Tag,
    int Limit,
    DiaryCursor? Cursor);

public sealed record DiaryPage(List<DiaryResponse> Items, string? NextCursor);
public sealed record DiaryCursor(int Version, DateOnly LocalDate, DateTime CreatedAt, Guid Id);
