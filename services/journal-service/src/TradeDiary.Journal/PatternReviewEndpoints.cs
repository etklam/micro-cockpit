using Npgsql;
using NpgsqlTypes;

static class PatternReviewEndpoints
{
    private const int MaxPatternReviewDays = 366;

    internal static void Map(RouteGroupBuilder journal)
    {
        journal.MapGet("/pattern-review", async (
            string range,
            DateOnly? from,
            DateOnly? to,
            HttpRequest request,
            NpgsqlDataSource db,
            TimeProvider timeProvider) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var timezone = request.HttpContext.User.FindFirst("timezone")?.Value ?? "UTC";
            var rollover = request.HttpContext.User.FindFirst("journal_day_rollover")?.Value ?? "00:00";
            DateOnly currentJournalDay;
            try { currentJournalDay = JournalDay.Resolve(timeProvider.GetUtcNow(), timezone, rollover); }
            catch (ArgumentException error) { return Results.Problem(error.Message, statusCode: 400); }

            var dates = ResolveRange(range, from, to, currentJournalDay);
            if (dates is null) return Results.Problem("invalid_range", statusCode: 400);
            var (start, end) = dates.Value;
            if (!JournalDay.TryUtcWindow(start, end, timezone, rollover, MaxPatternReviewDays, out var startUtc, out var endUtc))
                return Results.Problem("invalid_range", statusCode: 400);
            var dayCount = end.DayNumber - start.DayNumber + 1;
            var hasPrevious = start.DayNumber - dayCount >= DateOnly.MinValue.DayNumber;
            var previousStart = hasPrevious ? start.AddDays(-dayCount) : start;
            var previousEnd = hasPrevious ? start.AddDays(-1) : start;
            var previousStartUtc = startUtc;
            if (hasPrevious && !JournalDay.TryUtcWindow(previousStart, previousEnd, timezone, rollover, MaxPatternReviewDays, out previousStartUtc, out _))
                return Results.Problem("invalid_range", statusCode: 400);

            await using var count = db.CreateCommand("""
                SELECT count(*) FILTER (WHERE created_at >= $3),
                       count(*) FILTER (WHERE created_at < $3)
                FROM journal.expectation_reviews
                WHERE user_id=$1 AND deleted_at IS NULL AND created_at >= $2 AND created_at < $4
                """);
            count.Parameters.AddWithValue(userId);
            count.Parameters.AddWithValue(previousStartUtc);
            count.Parameters.AddWithValue(startUtc);
            count.Parameters.AddWithValue(endUtc);
            int denominator;
            int previousDenominator;
            await using (var countReader = await count.ExecuteReaderAsync())
            {
                await countReader.ReadAsync();
                denominator = Convert.ToInt32(countReader.GetInt64(0));
                previousDenominator = hasPrevious ? Convert.ToInt32(countReader.GetInt64(1)) : 0;
            }

            var labels = ExpectationReviewEndpoints.SystemIssues
                .Select(item => new MutablePatternLabel(ReasoningLabelKind.issue, item.Key, item.Value, true))
                .Concat(ExpectationReviewEndpoints.SystemStrengths
                    .Select(item => new MutablePatternLabel(ReasoningLabelKind.strength, item.Key, item.Value, true)))
                .ToDictionary(item => $"{item.Kind}:{item.Key}", StringComparer.Ordinal);

            await using (var confirmed = db.CreateCommand("""
                SELECT id,kind,label_key,label_name,is_system,is_confirmed
                FROM journal.confirmed_patterns
                WHERE user_id=$1
                """))
            {
                confirmed.Parameters.AddWithValue(userId);
                await using var confirmedReader = await confirmed.ExecuteReaderAsync();
                while (await confirmedReader.ReadAsync())
                {
                    var kind = Enum.Parse<ReasoningLabelKind>(confirmedReader.GetString(1));
                    var key = confirmedReader.GetString(2);
                    var dictionaryKey = $"{kind}:{key}";
                    if (!labels.TryGetValue(dictionaryKey, out var label))
                    {
                        label = new MutablePatternLabel(kind, key, confirmedReader.GetString(3), confirmedReader.GetBoolean(4));
                        labels.Add(dictionaryKey, label);
                    }
                    label.ConfirmedPatternId = confirmedReader.GetGuid(0);
                    label.PatternIsConfirmed = confirmedReader.GetBoolean(5);
                }
            }

            await using var command = db.CreateCommand("""
                WITH raw_evidence_sources AS (
                  SELECT l.kind,coalesce(l.system_key,l.custom_label_id::text) AS label_key,
                         coalesce(p.label_name,c.name,l.system_key) AS label_name,
                         coalesce(p.is_system,l.system_key IS NOT NULL) AS is_system,
                         l.review_id,p.id AS confirmed_pattern_id,true AS is_occurrence
                  FROM journal.expectation_review_labels l
                  JOIN journal.expectation_reviews r ON r.id=l.review_id AND r.user_id=l.user_id
                  LEFT JOIN journal.reasoning_labels c ON c.id=l.custom_label_id AND c.user_id=l.user_id
                  LEFT JOIN journal.confirmed_patterns p
                    ON p.user_id=l.user_id AND p.kind=l.kind
                   AND p.label_key=coalesce(l.system_key,l.custom_label_id::text)
                  WHERE l.user_id=$1 AND r.deleted_at IS NULL
                  UNION
                  SELECT p.kind,p.label_key,p.label_name,p.is_system,e.review_id,p.id,false
                  FROM journal.confirmed_pattern_evidence e
                  JOIN journal.confirmed_patterns p
                    ON p.id=e.confirmed_pattern_id AND p.user_id=e.user_id
                  WHERE e.user_id=$1
                ), evidence_sources AS (
                  SELECT DISTINCT ON (kind,label_key,review_id)
                         kind,label_key,label_name,is_system,review_id,confirmed_pattern_id,is_occurrence
                  FROM raw_evidence_sources
                  ORDER BY kind,label_key,review_id,is_occurrence DESC
                )
                SELECT s.kind,s.label_key,s.label_name,s.is_system,
                       r.expectation_id,r.id,o.journal_day,
                       coalesce(u.primary_subject->>'symbol',u.primary_subject->>'name',u.primary_subject->>'displayName',e.market),
                       e.expected_behavior,r.outcome,r.reasoning_quality,u.content,r.explanation,r.created_at,s.confirmed_pattern_id,s.is_occurrence
                FROM evidence_sources s
                JOIN journal.expectation_reviews r ON r.id=s.review_id AND r.user_id=$1
                JOIN journal.expectations e ON e.id=r.expectation_id AND e.user_id=r.user_id AND e.deleted_at IS NULL
                JOIN journal.observation_updates u ON u.id=e.observation_update_id AND u.user_id=e.user_id AND u.deleted_at IS NULL
                JOIN journal.market_observations o ON o.id=u.market_observation_id AND o.user_id=u.user_id AND o.deleted_at IS NULL
                WHERE r.user_id=$1 AND r.deleted_at IS NULL AND r.created_at >= $2 AND r.created_at < $4
                ORDER BY r.created_at DESC,r.expectation_id,s.label_key
                """);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(previousStartUtc);
            command.Parameters.AddWithValue(startUtc);
            command.Parameters.AddWithValue(endUtc);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var kind = Enum.Parse<ReasoningLabelKind>(reader.GetString(0));
                var key = reader.GetString(1);
                var dictionaryKey = $"{kind}:{key}";
                if (!labels.TryGetValue(dictionaryKey, out var label))
                {
                    label = new MutablePatternLabel(kind, key, reader.GetString(2), reader.GetBoolean(3));
                    labels.Add(dictionaryKey, label);
                }
                var reviewedAt = Utc(reader.GetDateTime(13));
                label.ConfirmedPatternId ??= reader.IsDBNull(14) ? null : reader.GetGuid(14);
                var evidence = new PatternEvidenceResponse(
                    reader.GetGuid(4), reader.GetGuid(5), reader.GetFieldValue<DateOnly>(6), reader.GetString(7),
                    reader.GetString(8), Enum.Parse<ExpectationOutcome>(reader.GetString(9)),
                    Enum.Parse<ReasoningQuality>(reader.GetString(10)), Shorten(reader.GetString(11), 280),
                    reader.IsDBNull(12) ? null : reader.GetString(12), reviewedAt,
                    $"/review?expectationId={reader.GetGuid(4)}");
                if (reviewedAt >= startUtc)
                {
                    label.AddEvidence(evidence);
                    if (reader.GetBoolean(15)) label.TrendEvidence.Add(evidence);
                }
                else if (reader.GetBoolean(15)) label.PreviousTrendEvidence.Add(evidence);
            }

            var response = labels.Values
                .Select(label => new PatternLabelResponse(
                    label.Kind, label.Key, label.Name, label.System,
                    label.Evidence.Count, denominator, label.ConfirmedPatternId,
                    label.PatternIsConfirmed,
                    label.FirstSeen, label.MostRecent, label.Evidence,
                    BuildTrend(label, start, end, previousStart, previousEnd, denominator, previousDenominator, hasPrevious)))
                .OrderByDescending(label => label.Count)
                .ThenBy(label => label.Kind)
                .ThenBy(label => label.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Results.Ok(new PatternReviewResponse(start, end, denominator, response));
        })
        .Produces<PatternReviewResponse>(200)
        .ProducesProblem(400)
        .ProducesProblem(401);

        journal.MapPost("/confirmed-patterns", async (
            ConfirmedPatternCreate input,
            HttpRequest request,
            NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var key = input.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
                return Results.Problem("invalid_pattern_key", statusCode: 400);

            var systemLabels = input.Kind == ReasoningLabelKind.issue
                ? ExpectationReviewEndpoints.SystemIssues
                : ExpectationReviewEndpoints.SystemStrengths;
            var system = systemLabels.TryGetValue(key, out var name);
            Guid? customLabelId = null;
            if (!system)
            {
                if (!Guid.TryParse(key, out var parsedId))
                    return Results.Problem("invalid_pattern_key", statusCode: 400);
                await using var custom = db.CreateCommand("""
                    SELECT c.name
                    FROM journal.reasoning_labels c
                    WHERE c.id=$1 AND c.user_id=$2 AND c.kind=$3 AND c.deleted_at IS NULL
                      AND 2 <= (
                        SELECT count(*) FROM journal.expectation_review_labels l
                        JOIN journal.expectation_reviews r ON r.id=l.review_id AND r.user_id=l.user_id
                        WHERE l.custom_label_id=c.id AND l.user_id=c.user_id AND r.deleted_at IS NULL
                      )
                    """);
                custom.Parameters.AddWithValue(parsedId);
                custom.Parameters.AddWithValue(userId);
                custom.Parameters.AddWithValue(input.Kind.ToString());
                name = await custom.ExecuteScalarAsync() as string;
                if (name is null) return Results.Problem("not_found", statusCode: 404);
                customLabelId = parsedId;
            }
            else
            {
                await using var evidence = db.CreateCommand("""
                    SELECT 2 <= count(*)
                      FROM journal.expectation_review_labels l
                      JOIN journal.expectation_reviews r ON r.id=l.review_id AND r.user_id=l.user_id
                      WHERE l.user_id=$1 AND l.kind=$2 AND l.system_key=$3 AND r.deleted_at IS NULL
                    """);
                evidence.Parameters.AddWithValue(userId);
                evidence.Parameters.AddWithValue(input.Kind.ToString());
                evidence.Parameters.AddWithValue(key);
                if (!(bool)(await evidence.ExecuteScalarAsync())!)
                    return Results.Problem("not_found", statusCode: 404);
            }

            await using var connection = await db.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand("""
                INSERT INTO journal.confirmed_patterns(id,user_id,kind,label_key,label_name,is_system,custom_label_id)
                VALUES($1,$2,$3,$4,$5,$6,$7)
                ON CONFLICT (user_id,kind,label_key) DO UPDATE SET
                    label_name=excluded.label_name,
                    is_confirmed=true,
                    confirmed_at=CASE WHEN NOT confirmed_patterns.is_confirmed THEN now() ELSE confirmed_patterns.confirmed_at END,
                    unconfirmed_at=NULL,
                    updated_at=CASE WHEN NOT confirmed_patterns.is_confirmed THEN now() ELSE confirmed_patterns.updated_at END
                RETURNING id,label_name,is_system,is_confirmed,created_at,confirmed_at,unconfirmed_at,updated_at
                """, connection, tx);
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(input.Kind.ToString());
            command.Parameters.AddWithValue(key);
            command.Parameters.AddWithValue(name!);
            command.Parameters.AddWithValue(system);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)customLabelId ?? DBNull.Value });
            Guid patternId;
            string patternName;
            bool patternIsSystem;
            bool patternIsConfirmed;
            DateTime firstConfirmedAt;
            DateTime confirmedAt;
            DateTime? unconfirmedAt;
            DateTime updatedAt;
            await using (var reader = await command.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                patternId = reader.GetGuid(0);
                patternName = reader.GetString(1);
                patternIsSystem = reader.GetBoolean(2);
                patternIsConfirmed = reader.GetBoolean(3);
                firstConfirmedAt = Utc(reader.GetDateTime(4));
                confirmedAt = Utc(reader.GetDateTime(5));
                unconfirmedAt = reader.IsDBNull(6) ? null : Utc(reader.GetDateTime(6));
                updatedAt = Utc(reader.GetDateTime(7));
            }

            var evidenceSql = system
                ? """
                  INSERT INTO journal.confirmed_pattern_evidence(confirmed_pattern_id,user_id,review_id)
                  SELECT $1,l.user_id,l.review_id
                  FROM journal.expectation_review_labels l
                  JOIN journal.expectation_reviews r ON r.id=l.review_id AND r.user_id=l.user_id
                  WHERE l.user_id=$2 AND l.kind=$3 AND l.system_key=$4 AND r.deleted_at IS NULL
                  ON CONFLICT DO NOTHING
                  """
                : """
                  INSERT INTO journal.confirmed_pattern_evidence(confirmed_pattern_id,user_id,review_id)
                  SELECT $1,l.user_id,l.review_id
                  FROM journal.expectation_review_labels l
                  JOIN journal.expectation_reviews r ON r.id=l.review_id AND r.user_id=l.user_id
                  WHERE l.user_id=$2 AND l.kind=$3 AND l.custom_label_id=$4 AND r.deleted_at IS NULL
                  ON CONFLICT DO NOTHING
                  """;
            await using (var evidence = new NpgsqlCommand(evidenceSql, connection, tx))
            {
                evidence.Parameters.AddWithValue(patternId);
                evidence.Parameters.AddWithValue(userId);
                evidence.Parameters.AddWithValue(input.Kind.ToString());
                if (system) evidence.Parameters.AddWithValue(key);
                else evidence.Parameters.AddWithValue(customLabelId!.Value);
                await evidence.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return Results.Ok(new ConfirmedPatternResponse(
                patternId, input.Kind, key, patternName, patternIsSystem, patternIsConfirmed,
                firstConfirmedAt, confirmedAt, unconfirmedAt, updatedAt));
        }).Produces<ConfirmedPatternResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

        journal.MapDelete("/confirmed-patterns/{id:guid}", async (
            Guid id,
            HttpRequest request,
            NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var command = db.CreateCommand("""
                UPDATE journal.confirmed_patterns
                SET is_confirmed=false,unconfirmed_at=now(),updated_at=now()
                WHERE id=$1 AND user_id=$2 AND is_confirmed
                """);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(userId);
            if (await command.ExecuteNonQueryAsync() > 0) return Results.NoContent();

            await using var exists = db.CreateCommand(
                "SELECT EXISTS(SELECT 1 FROM journal.confirmed_patterns WHERE id=$1 AND user_id=$2)");
            exists.Parameters.AddWithValue(id);
            exists.Parameters.AddWithValue(userId);
            return (bool)(await exists.ExecuteScalarAsync())!
                ? Results.NoContent()
                : Results.Problem("not_found", statusCode: 404);
        }).Produces(204).ProducesProblem(401).ProducesProblem(404);

        journal.MapGet("/discipline-principles", async (HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            return Results.Ok(new CollectionResponse<DisciplinePrincipleResponse>(await ListPrinciples(db, userId)));
        }).Produces<CollectionResponse<DisciplinePrincipleResponse>>(200).ProducesProblem(401);

        journal.MapGet("/discipline-principles/today", async (HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var principle = (await ListPrinciples(db, userId)).SingleOrDefault(item => item.SelectedForToday);
            return principle is null ? Results.Problem("not_found", statusCode: 404) : Results.Ok(principle);
        }).Produces<DisciplinePrincipleResponse>(200).ProducesProblem(401).ProducesProblem(404);

        journal.MapPost("/discipline-principles", async (DisciplinePrincipleCreate input, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var content = input.Content?.Trim();
            if (string.IsNullOrWhiteSpace(content) || content.Length > 280)
                return Results.Problem("invalid_content", statusCode: 400);
            string? patternLabel = null;
            if (input.ConfirmedPatternId is { } patternId)
            {
                await using var pattern = db.CreateCommand("SELECT label_name FROM journal.confirmed_patterns WHERE id=$1 AND user_id=$2 AND is_confirmed");
                pattern.Parameters.AddWithValue(patternId);
                pattern.Parameters.AddWithValue(userId);
                patternLabel = await pattern.ExecuteScalarAsync() as string;
                if (patternLabel is null) return Results.Problem("not_found", statusCode: 404);
            }
            var id = Guid.NewGuid();
            await using var command = db.CreateCommand("""
                INSERT INTO journal.discipline_principles(id,user_id,content,confirmed_pattern_id)
                VALUES($1,$2,$3,$4)
                RETURNING created_at,updated_at
                """);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(content);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)input.ConfirmedPatternId ?? DBNull.Value });
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            return Results.Created($"/internal/discipline-principles/{id}", new DisciplinePrincipleResponse(
                id, content, DisciplinePrincipleStatus.active, false, input.ConfirmedPatternId, patternLabel,
                Utc(reader.GetDateTime(0)), Utc(reader.GetDateTime(1))));
        }).Produces<DisciplinePrincipleResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

        journal.MapPut("/discipline-principles/{id:guid}", async (
            Guid id,
            DisciplinePrincipleUpdate input,
            HttpRequest request,
            NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var content = input.Content?.Trim();
            if (string.IsNullOrWhiteSpace(content) || content.Length > 280)
                return Results.Problem("invalid_content", statusCode: 400);
            await using var command = db.CreateCommand("""
                UPDATE journal.discipline_principles
                SET content=$3,status=$4,
                    selected_for_today=selected_for_today AND $4='active',
                    updated_at=now()
                WHERE id=$1 AND user_id=$2
                """);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(content);
            command.Parameters.AddWithValue(input.Status.ToString());
            return await command.ExecuteNonQueryAsync() == 0
                ? Results.Problem("not_found", statusCode: 404)
                : Results.NoContent();
        }).Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

        journal.MapPost("/discipline-principles/{id:guid}/select", async (
            Guid id,
            HttpRequest request,
            NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var connection = await db.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using var exists = new NpgsqlCommand("""
                SELECT EXISTS(
                    SELECT 1 FROM journal.discipline_principles
                    WHERE id=$1 AND user_id=$2 AND status='active'
                )
                """, connection, tx);
            exists.Parameters.AddWithValue(id);
            exists.Parameters.AddWithValue(userId);
            if (!(bool)(await exists.ExecuteScalarAsync())!)
                return Results.Problem("not_found", statusCode: 404);
            await using var clear = new NpgsqlCommand("""
                UPDATE journal.discipline_principles
                SET selected_for_today=false,updated_at=now()
                WHERE user_id=$1 AND selected_for_today
                """, connection, tx);
            clear.Parameters.AddWithValue(userId);
            await clear.ExecuteNonQueryAsync();
            await using var select = new NpgsqlCommand("""
                UPDATE journal.discipline_principles
                SET selected_for_today=true,updated_at=now()
                WHERE id=$1 AND user_id=$2
                """, connection, tx);
            select.Parameters.AddWithValue(id);
            select.Parameters.AddWithValue(userId);
            await select.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        }).Produces(204).ProducesProblem(401).ProducesProblem(404);
    }

    private static (DateOnly Start, DateOnly End)? ResolveRange(
        string range, DateOnly? from, DateOnly? to, DateOnly currentJournalDay)
    {
        return range switch
        {
            "weekly" when from is null && to is null => (currentJournalDay.AddDays(-6), currentJournalDay),
            "monthly" when from is null && to is null => (new DateOnly(currentJournalDay.Year, currentJournalDay.Month, 1), currentJournalDay),
            "custom" when from is not null && to is not null => (from.Value, to.Value),
            _ => null,
        };
    }

    private static async Task<List<DisciplinePrincipleResponse>> ListPrinciples(NpgsqlDataSource db, Guid userId)
    {
        await using var command = db.CreateCommand("""
            SELECT d.id,d.content,d.status,d.selected_for_today,d.confirmed_pattern_id,p.label_name,d.created_at,d.updated_at
            FROM journal.discipline_principles d
            LEFT JOIN journal.confirmed_patterns p ON p.id=d.confirmed_pattern_id AND p.user_id=d.user_id
            WHERE d.user_id=$1
            ORDER BY d.selected_for_today DESC,
                     CASE d.status WHEN 'active' THEN 0 WHEN 'disabled' THEN 1 ELSE 2 END,
                     d.created_at DESC,d.id
            """);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<DisciplinePrincipleResponse>();
        while (await reader.ReadAsync())
            result.Add(new(
                reader.GetGuid(0), reader.GetString(1),
                Enum.Parse<DisciplinePrincipleStatus>(reader.GetString(2)),
                reader.GetBoolean(3), reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), Utc(reader.GetDateTime(6)), Utc(reader.GetDateTime(7))));
        return result;
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static string Shorten(string value, int max) => value.Length <= max ? value : $"{value[..(max - 1)].TrimEnd()}…";

    private static PatternTrendResponse BuildTrend(
        MutablePatternLabel label,
        DateOnly start,
        DateOnly end,
        DateOnly previousStart,
        DateOnly previousEnd,
        int denominator,
        int previousDenominator,
        bool hasPrevious)
    {
        var current = new PatternTrendBucketResponse(start, end, label.TrendEvidence.Count, denominator, label.TrendEvidence);
        if (!hasPrevious)
            return new(PatternTrendStatus.insufficient_evidence, null, current, null);

        var previous = new PatternTrendBucketResponse(
            previousStart, previousEnd, label.PreviousTrendEvidence.Count, previousDenominator, label.PreviousTrendEvidence);
        if (denominator < 5 || previousDenominator < 5 || label.TrendEvidence.Count + label.PreviousTrendEvidence.Count == 0)
            return new(PatternTrendStatus.insufficient_evidence, null, current, previous);

        var comparison = (long)label.TrendEvidence.Count * previousDenominator
            - (long)label.PreviousTrendEvidence.Count * denominator;
        var direction = comparison > 0
            ? PatternTrendDirection.higher
            : comparison < 0 ? PatternTrendDirection.lower : PatternTrendDirection.same;
        return new(PatternTrendStatus.supported, direction, current, previous);
    }

    private sealed class MutablePatternLabel(ReasoningLabelKind kind, string key, string name, bool system)
    {
        internal ReasoningLabelKind Kind { get; } = kind;
        internal string Key { get; } = key;
        internal string Name { get; } = name;
        internal bool System { get; } = system;
        internal Guid? ConfirmedPatternId { get; set; }
        internal bool PatternIsConfirmed { get; set; }
        internal DateTime? FirstSeen { get; private set; }
        internal DateTime? MostRecent { get; private set; }
        internal List<PatternEvidenceResponse> Evidence { get; } = [];
        internal List<PatternEvidenceResponse> TrendEvidence { get; } = [];
        internal List<PatternEvidenceResponse> PreviousTrendEvidence { get; } = [];

        internal void AddEvidence(PatternEvidenceResponse evidence)
        {
            Evidence.Add(evidence);
            FirstSeen = FirstSeen is null || evidence.ReviewedAt < FirstSeen ? evidence.ReviewedAt : FirstSeen;
            MostRecent = MostRecent is null || evidence.ReviewedAt > MostRecent ? evidence.ReviewedAt : MostRecent;
        }
    }
}
