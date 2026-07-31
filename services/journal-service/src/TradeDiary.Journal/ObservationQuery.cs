using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

static class ObservationQuery
{
    private const int CursorVersion = 1;
    private const int MaxQueryLength = 200;
    private const char LikeEscape = '\\';
    private static readonly JsonSerializerOptions CursorJson = new(JsonSerializerDefaults.Web);

    internal static string? Validate(
        string? query,
        DateOnly? from,
        DateOnly? to,
        ObservationSubjectType? subjectType,
        string? subject,
        Guid? instrumentId,
        string? market,
        string? symbol,
        string? tag,
        string? author,
        int limit,
        string? cursor,
        out ObservationListQuery parsed)
    {
        parsed = default!;
        if (from is { } start && to is { } end && start > end) return "invalid_date_range";
        if (limit is < 1 or > 100) return "invalid_limit";

        var keyword = Trim(query);
        if (keyword?.Length > MaxQueryLength) return "invalid_query";
        var subjectName = Trim(subject);
        if ((subjectType is null) != (subjectName is null) || subjectName?.Length > 120 || subjectType == ObservationSubjectType.instrument)
            return "invalid_subject_filter";
        var normalizedMarket = Trim(market)?.ToUpperInvariant();
        var normalizedSymbol = Trim(symbol)?.ToUpperInvariant();
        if ((normalizedMarket is null) != (normalizedSymbol is null) || normalizedMarket?.Length > 32 || normalizedSymbol?.Length > 24)
            return "invalid_instrument_filter";

        string? normalizedTag = null;
        if (!string.IsNullOrWhiteSpace(tag) && !ObservationTags.TryNormalizeOne(tag, out normalizedTag, out var tagError)) return tagError;

        Guid? authorId = null;
        var authorValue = Trim(author);
        if (authorValue is not null && authorValue is not ("current" or "me") && !Guid.TryParse(authorValue, out var parsedAuthor)) return "invalid_author";
        if (authorValue is not null && authorValue is not ("current" or "me")) authorId = Guid.Parse(authorValue);

        ObservationCursor? parsedCursor = null;
        if (cursor is not null && !TryDecodeCursor(cursor, out parsedCursor)) return "invalid_cursor";
        parsed = new(keyword, from, to, subjectType, subjectName, instrumentId, normalizedMarket, normalizedSymbol, normalizedTag, authorId, limit, parsedCursor);
        return null;
    }

    internal static async Task<ObservationSearchPage> ReadAsync(NpgsqlDataSource db, Guid userId, ObservationListQuery query)
    {
        if (query.AuthorId is { } authorId && authorId != userId) return new([], null);
        var sql = new StringBuilder("""
            SELECT o.id,o.journal_day,o.user_id,
                   u.id,u.content,u.recorded_at,u.updated_at,u.signal,u.interpretation,u.mental_state,u.tags,
                   u.primary_subject::text,u.related_subjects::text,u.evidence::text
            FROM journal.market_observations o
            JOIN journal.observation_updates u
              ON u.market_observation_id=o.id AND u.user_id=o.user_id AND u.deleted_at IS NULL
            WHERE o.user_id=$1 AND o.deleted_at IS NULL
            """);
        var parameters = new List<object?> { userId };
        var index = 2;

        if (query.From is { } from) Add(" AND o.journal_day >= ", from);
        if (query.To is { } to) Add(" AND o.journal_day <= ", to);
        if (query.Keyword is { } keyword)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND u.content ILIKE ${index} ESCAPE '{LikeEscape}'");
            parameters.Add("%" + EscapeLike(keyword) + "%"); index++;
        }
        if (query.SubjectType is { } subjectType && query.Subject is { } subject)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND journal.observation_subject_search_key(u.primary_subject,u.related_subjects) @> jsonb_build_array(jsonb_build_object('type',${index},'name',lower(${index + 1})))");
            parameters.Add(subjectType.ToString().ToLowerInvariant()); parameters.Add(subject); index += 2;
        }
        if (query.InstrumentId is { } instrumentId)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND journal.observation_subject_search_key(u.primary_subject,u.related_subjects) @> jsonb_build_array(jsonb_build_object('instrumentId',${index}))");
            parameters.Add(instrumentId.ToString()); index++;
        }
        if (query.Market is { } market && query.Symbol is { } symbol)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND journal.observation_subject_search_key(u.primary_subject,u.related_subjects) @> jsonb_build_array(jsonb_build_object('type','instrument','market',${index},'symbol',${index + 1}))");
            parameters.Add(market); parameters.Add(symbol); index += 2;
        }
        if (query.Tag is { } tag)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND ${index}=ANY(u.tags)");
            parameters.Add(tag); index++;
        }
        if (query.Cursor is { } cursor)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND (o.journal_day,u.recorded_at,u.id)<(${index},${index + 1},${index + 2})");
            parameters.Add(cursor.JournalDay); parameters.Add(cursor.RecordedAt); parameters.Add(cursor.Id); index += 3;
        }
        sql.Append(CultureInfo.InvariantCulture, $" ORDER BY o.journal_day DESC,u.recorded_at DESC,u.id DESC LIMIT ${index}");
        parameters.Add(query.Limit + 1);

        await using var command = db.CreateCommand(sql.ToString());
        foreach (var value in parameters) command.Parameters.Add(Parameter(value));
        var rows = new List<(ObservationSearchItemResponse Item, ObservationCursor Cursor)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var update = ObservationEnrichment.Read(reader, 3);
            var item = new ObservationSearchItemResponse(reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1), reader.GetGuid(2), update);
            rows.Add((item, new(CursorVersion, item.JournalDay, update.RecordedAt, update.Id)));
        }
        var hasMore = rows.Count > query.Limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return new(rows.Select(row => row.Item).ToArray(), hasMore ? EncodeCursor(rows[^1].Cursor) : null);

        void Add(string fragment, object value)
        {
            sql.Append(CultureInfo.InvariantCulture, $"{fragment}${index}");
            parameters.Add(value); index++;
        }
    }

    private static NpgsqlParameter Parameter(object? value) => value switch
    {
        DateOnly date => new() { NpgsqlDbType = NpgsqlDbType.Date, Value = date },
        DateTime dateTime => new() { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc) },
        Guid id => new() { NpgsqlDbType = NpgsqlDbType.Uuid, Value = id },
        int number => new() { NpgsqlDbType = NpgsqlDbType.Integer, Value = number },
        _ => new() { Value = value ?? DBNull.Value },
    };

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string EscapeLike(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '%' or '_' or LikeEscape) builder.Append(LikeEscape);
            builder.Append(character);
        }
        return builder.ToString();
    }
    private static string EncodeCursor(ObservationCursor cursor) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor, CursorJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool TryDecodeCursor(string value, out ObservationCursor? cursor)
    {
        cursor = null;
        try
        {
            var encoded = value.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            cursor = JsonSerializer.Deserialize<ObservationCursor>(Convert.FromBase64String(encoded), CursorJson);
            return cursor is { Version: CursorVersion } decoded && decoded.JournalDay != default && decoded.RecordedAt != default && decoded.Id != Guid.Empty;
        }
        catch (Exception error) when (error is FormatException or JsonException) { return false; }
    }
}

sealed record ObservationListQuery(string? Keyword, DateOnly? From, DateOnly? To, ObservationSubjectType? SubjectType, string? Subject, Guid? InstrumentId, string? Market, string? Symbol, string? Tag, Guid? AuthorId, int Limit, ObservationCursor? Cursor);
sealed record ObservationCursor(int Version, DateOnly JournalDay, DateTime RecordedAt, Guid Id);
