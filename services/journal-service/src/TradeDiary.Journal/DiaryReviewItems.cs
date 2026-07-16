using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

public static class DiaryReviewItems
{
    private static readonly JsonSerializerOptions CursorJson = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlySet<string> Statuses = new HashSet<string> { "all", "reviewed", "unreviewed" };
    private static readonly IReadOnlySet<string> Assessments = new HashSet<string> { "all", "good", "mixed", "poor" };

    public static string? Validate(DateOnly from, DateOnly to, string status, string assessment, string? tag, int limit, string? cursor, out DiaryReviewItemsCursor? parsedCursor)
    {
        parsedCursor = null;
        if (to < from || to.DayNumber - from.DayNumber >= 62) return "invalid_date_range";
        if (!Statuses.Contains(status)) return "invalid_review_status";
        if (!Assessments.Contains(assessment)) return "invalid_process_assessment";
        if (tag is not null && !DiaryReviewRules.MistakeTags.Contains(tag)) return "invalid_mistake_tag";
        if (limit is < 1 or > 100) return "invalid_limit";
        if (cursor is not null && !TryDecode(cursor, out parsedCursor)) return "invalid_cursor";
        return null;
    }

    public static async Task<DiaryReviewItemsResponse> ReadAsync(NpgsqlDataSource db, Guid userId, DateOnly from, DateOnly to, string status, string assessment, string? tag, int limit, DiaryReviewItemsCursor? cursor)
    {
        await using var command = db.CreateCommand("""
            SELECT d.id,d.local_date,d.title,left(d.content,240),d.created_at,
                   r.diary_id,r.process_assessment,r.emotion,r.discipline_score,r.execution_score,
                   coalesce(r.mistake_tags,'{{}}'::text[]),r.lesson,r.next_action,r.updated_at
            FROM journal.diaries d
            LEFT JOIN journal.diary_reviews r ON r.diary_id=d.id AND r.user_id=d.user_id
            WHERE d.user_id=$1 AND d.deleted_at IS NULL AND d.local_date BETWEEN $2 AND $3
              AND ($4='all' OR ($4='reviewed' AND r.diary_id IS NOT NULL) OR ($4='unreviewed' AND r.diary_id IS NULL))
              AND ($5='all' OR r.process_assessment=$5)
              AND ($6::text IS NULL OR $6=ANY(r.mistake_tags))
              AND (NOT $7 OR (d.local_date,d.created_at,d.id) < ($8,$9,$10))
            ORDER BY d.local_date DESC,d.created_at DESC,d.id DESC
            LIMIT $11
            """);
        command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(from); command.Parameters.AddWithValue(to);
        command.Parameters.AddWithValue(status); command.Parameters.AddWithValue(assessment);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = tag is null ? DBNull.Value : tag });
        command.Parameters.AddWithValue(cursor is not null);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Date, Value = cursor is null ? DBNull.Value : cursor.LocalDate });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = cursor is null ? DBNull.Value : cursor.CreatedAt });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = cursor is null ? DBNull.Value : cursor.Id });
        command.Parameters.AddWithValue(limit + 1);

        var rows = new List<(DiaryReviewItemResponse Item, DiaryReviewItemsCursor Cursor)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var item = new DiaryReviewItemResponse(
                reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(5) ? DiaryReviewStatus.unreviewed : DiaryReviewStatus.reviewed,
                reader.IsDBNull(6) ? null : Enum.Parse<DiaryReviewProcessAssessment>(reader.GetString(6)), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt16(8), reader.IsDBNull(9) ? null : reader.GetInt16(9),
                reader.GetFieldValue<string[]>(10), reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetDateTime(13));
            rows.Add((item, new DiaryReviewItemsCursor(reader.GetFieldValue<DateOnly>(1), reader.GetDateTime(4), reader.GetGuid(0))));
        }
        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return new DiaryReviewItemsResponse(rows.Select(row => row.Item).ToArray(), hasMore ? Encode(rows[^1].Cursor) : null);
    }

    private static string Encode(DiaryReviewItemsCursor cursor) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor, CursorJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecode(string value, out DiaryReviewItemsCursor? cursor)
    {
        cursor = null;
        try
        {
            var encoded = value.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            cursor = JsonSerializer.Deserialize<DiaryReviewItemsCursor>(Convert.FromBase64String(encoded), CursorJson);
            return cursor is not null && cursor.Id != Guid.Empty && cursor.LocalDate != default && cursor.CreatedAt != default;
        }
        catch (Exception error) when (error is FormatException or JsonException) { return false; }
    }
}

public sealed record DiaryReviewItemsResponse(IReadOnlyList<DiaryReviewItemResponse> Items, string? NextCursor);
public enum DiaryReviewStatus { reviewed, unreviewed }
public enum DiaryReviewFilterStatus { all, reviewed, unreviewed }
public enum DiaryReviewProcessAssessment { good, mixed, poor }
public enum DiaryReviewAssessmentFilter { all, good, mixed, poor }
public sealed record DiaryReviewItemResponse(
    Guid DiaryId, DateOnly LocalDate, string Title, string ContentPreview, DiaryReviewStatus ReviewStatus,
    DiaryReviewProcessAssessment? ProcessAssessment, string? Emotion, short? DisciplineScore, short? ExecutionScore,
    IReadOnlyList<string> MistakeTags, string? Lesson, string? NextAction, DateTime? ReviewUpdatedAt);
public sealed record DiaryReviewItemsCursor(DateOnly LocalDate, DateTime CreatedAt, Guid Id);
