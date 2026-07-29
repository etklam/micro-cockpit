using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using TradeDiary.Authorization;

static class AccessGrantEndpoints
{
    private const int CursorVersion = 1;
    private static readonly JsonSerializerOptions CursorJson = new(JsonSerializerDefaults.Web);

    internal static void Map(WebApplication app)
    {
        var human = app.MapGroup("/internal/access-grants").RequireAuthorization();
        human.MapGet("", ListAsync)
            .Produces<CollectionResponse<AccessGrantResponse>>(200).ProducesProblem(401);
        human.MapPost("", CreateAsync)
            .Produces<AccessGrantResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);
        human.MapDelete("/{id:guid}", RevokeAsync)
            .Produces(204).ProducesProblem(401).ProducesProblem(404);

        app.MapGet("/internal/agent/granted-records", QueryAsync)
            .RequireAuthorization(TradeDiaryPolicies.AgentRead)
            .Produces<GrantedRecordPage>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);
        app.MapGet("/internal/agent/journal-changes", IncrementalAsync)
            .RequireAuthorization(TradeDiaryPolicies.AgentRead)
            .Produces<GrantedChangePage>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(410);
    }

    private static async Task<IResult> ListAsync(HttpRequest request, NpgsqlDataSource db)
    {
        if (!JournalAccess.TryUser(request, out var ownerId)) return Results.Unauthorized();
        await using var command = db.CreateCommand("""
            SELECT id,agent_user_id,mode,from_date,to_date,subject_type,subject_name,instrument_id,
                   expires_at,revoked_at,created_at
            FROM journal.agent_access_grants
            WHERE owner_user_id=$1
            ORDER BY created_at DESC,id DESC
            """);
        command.Parameters.AddWithValue(ownerId);
        await using var reader = await command.ExecuteReaderAsync();
        var items = new List<AccessGrantResponse>();
        while (await reader.ReadAsync()) items.Add(ReadGrant(reader));
        return Results.Ok(new CollectionResponse<AccessGrantResponse>(items));
    }

    private static async Task<IResult> CreateAsync(
        AccessGrantWrite input,
        HttpRequest request,
        NpgsqlDataSource db,
        IHttpClientFactory clients,
        TimeProvider timeProvider,
        IConfiguration configuration)
    {
        if (!JournalAccess.TryUser(request, out var ownerId)) return Results.Unauthorized();
        var error = Validate(input, timeProvider.GetUtcNow());
        if (error is not null) return Results.Problem(error, statusCode: 400);
        if (!await IsManagedAgentAsync(clients, configuration, ownerId, input.AgentUserId, request.HttpContext.RequestAborted))
            return Results.Problem("agent_not_managed", statusCode: 403);

        var id = Guid.NewGuid();
        await using var connection = await db.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO journal.agent_access_grants
                (id,owner_user_id,agent_user_id,mode,from_date,to_date,subject_type,subject_name,instrument_id,expires_at)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)
            RETURNING id,agent_user_id,mode,from_date,to_date,subject_type,subject_name,instrument_id,
                      expires_at,revoked_at,created_at
            """, connection, tx))
        {
            AddGrantParameters(insert, id, ownerId, input);
            await using var reader = await insert.ExecuteReaderAsync();
            await reader.ReadAsync();
            var response = ReadGrant(reader);
            await reader.CloseAsync();
            if (input.Mode == AccessGrantMode.@fixed)
                await CaptureFixedClosureAsync(connection, tx, id, ownerId, input);
            await tx.CommitAsync();
            return Results.Created($"/internal/access-grants/{id}", response);
        }
    }

    private static async Task<IResult> RevokeAsync(Guid id, HttpRequest request, NpgsqlDataSource db, TimeProvider timeProvider)
    {
        if (!JournalAccess.TryUser(request, out var ownerId)) return Results.Unauthorized();
        await using var command = db.CreateCommand("""
            UPDATE journal.agent_access_grants
            SET revoked_at=COALESCE(revoked_at,$3)
            WHERE id=$1 AND owner_user_id=$2
            """);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(ownerId);
        command.Parameters.AddWithValue(timeProvider.GetUtcNow().UtcDateTime);
        return await command.ExecuteNonQueryAsync() == 0
            ? Results.Problem("not_found", statusCode: 404)
            : Results.NoContent();
    }

    private static async Task<IResult> QueryAsync(
        HttpRequest request,
        NpgsqlDataSource db,
        TimeProvider timeProvider,
        DateOnly? from = null,
        DateOnly? to = null,
        string? subjectType = null,
        string? subject = null,
        Guid? instrumentId = null,
        string? tag = null,
        string? reviewReadiness = null,
        Guid? author = null,
        string? cursor = null,
        int limit = 20)
    {
        if (!JournalAccess.TryUser(request, out var agentId)) return Results.Unauthorized();
        if (request.HttpContext.User.FindFirst("account_type")?.Value != "agent")
            return Results.Problem("agent_required", statusCode: 403);
        ObservationSubjectType? subjectFilter = null;
        if (subjectType is not null)
        {
            if (!Enum.TryParse<ObservationSubjectType>(subjectType, out var parsedSubjectType)
                || parsedSubjectType == ObservationSubjectType.instrument)
                return Results.Problem("invalid_filter", statusCode: 400);
            subjectFilter = parsedSubjectType;
        }
        ExpectationReadiness? readinessFilter = null;
        if (reviewReadiness is not null)
        {
            if (!Enum.TryParse<ExpectationReadiness>(reviewReadiness, out var parsedReadiness))
                return Results.Problem("invalid_filter", statusCode: 400);
            readinessFilter = parsedReadiness;
        }
        if (from is { } start && to is { } end && start > end
            || limit is < 1 or > 100
            || (subjectType is null) != string.IsNullOrWhiteSpace(subject)
            || subject?.Trim().Length > 120)
            return Results.Problem("invalid_filter", statusCode: 400);
        string? normalizedTag = null;
        if (!string.IsNullOrWhiteSpace(tag) && !ObservationTags.TryNormalizeOne(tag, out normalizedTag, out _))
            return Results.Problem("invalid_tag", statusCode: 400);
        GrantedCursor? parsedCursor = null;
        if (cursor is not null && !TryDecodeCursor(cursor, out parsedCursor))
            return Results.Problem("invalid_cursor", statusCode: 400);

        var page = await ReadGrantedPageAsync(
            db, agentId, timeProvider.GetUtcNow(), from, to, subjectFilter, subject?.Trim(),
            instrumentId, normalizedTag, readinessFilter, author, parsedCursor, limit);
        return Results.Ok(page);
    }

    private static async Task<GrantedRecordPage> ReadGrantedPageAsync(
        NpgsqlDataSource db,
        Guid agentId,
        DateTimeOffset now,
        DateOnly? from,
        DateOnly? to,
        ObservationSubjectType? subjectType,
        string? subject,
        Guid? instrumentId,
        string? tag,
        ExpectationReadiness? readiness,
        Guid? author,
        GrantedCursor? cursor,
        int limit)
    {
        var sql = new StringBuilder("""
            SELECT DISTINCT o.id,o.user_id,o.journal_day
            FROM journal.market_observations o
            WHERE o.deleted_at IS NULL
              AND EXISTS (
                SELECT 1
                FROM journal.agent_access_grants g
                WHERE g.agent_user_id=$1 AND g.owner_user_id=o.user_id
                  AND g.revoked_at IS NULL AND (g.expires_at IS NULL OR g.expires_at>$2)
                  AND o.journal_day BETWEEN g.from_date AND g.to_date
                  AND (
                    (g.mode='fixed' AND EXISTS (
                        SELECT 1 FROM journal.agent_access_grant_records gr
                        WHERE gr.grant_id=g.id AND gr.record_type='market_observation' AND gr.record_id=o.id
                    ))
                    OR
                    (g.mode='ongoing' AND (
                        (g.subject_type IS NULL AND g.instrument_id IS NULL)
                        OR EXISTS (
                            SELECT 1 FROM journal.observation_updates gu
                            WHERE gu.market_observation_id=o.id AND gu.user_id=o.user_id AND gu.deleted_at IS NULL
                              AND journal.subject_matches(gu.primary_subject,gu.related_subjects,g.subject_type,g.subject_name,g.instrument_id)
                        )
                    ))
                  )
              )
            """);
        var values = new List<object?> { agentId, now.UtcDateTime };
        var index = 3;
        void Add(string fragment, object value)
        {
            sql.Append(CultureInfo.InvariantCulture, $"{fragment}${index}");
            values.Add(value); index++;
        }
        if (from is { } fromDate) Add(" AND o.journal_day>=", fromDate);
        if (to is { } toDate) Add(" AND o.journal_day<=", toDate);
        if (author is { } authorId) Add(" AND o.user_id=", authorId);
        if (subjectType is { } type && subject is not null)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND EXISTS (SELECT 1 FROM journal.observation_updates fu WHERE fu.market_observation_id=o.id AND fu.user_id=o.user_id AND fu.deleted_at IS NULL AND journal.subject_matches(fu.primary_subject,fu.related_subjects,${index},${index + 1},NULL))");
            values.Add(type.ToString()); values.Add(subject); index += 2;
        }
        if (instrumentId is { } instrument)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND EXISTS (SELECT 1 FROM journal.observation_updates fu WHERE fu.market_observation_id=o.id AND fu.user_id=o.user_id AND fu.deleted_at IS NULL AND journal.subject_matches(fu.primary_subject,fu.related_subjects,NULL,NULL,${index}))");
            values.Add(instrument); index++;
        }
        if (tag is not null)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND EXISTS (SELECT 1 FROM journal.observation_updates fu WHERE fu.market_observation_id=o.id AND fu.user_id=o.user_id AND fu.deleted_at IS NULL AND ${index}=ANY(fu.tags))");
            values.Add(tag); index++;
        }
        if (readiness is { } status)
        {
            var predicate = status switch
            {
                ExpectationReadiness.active => $"e.invalidated_at IS NULL AND e.deadline>${index} AND r.id IS NULL",
                ExpectationReadiness.ready_for_review => $"(e.invalidated_at IS NOT NULL OR e.deadline<=${index}) AND r.id IS NULL",
                _ => "r.id IS NOT NULL",
            };
            sql.Append(CultureInfo.InvariantCulture, $" AND EXISTS (SELECT 1 FROM journal.observation_updates ru JOIN journal.expectations e ON e.observation_update_id=ru.id AND e.user_id=ru.user_id AND e.deleted_at IS NULL LEFT JOIN journal.expectation_reviews r ON r.expectation_id=e.id WHERE ru.market_observation_id=o.id AND ru.user_id=o.user_id AND ru.deleted_at IS NULL AND {predicate})");
            if (status != ExpectationReadiness.reviewed) { values.Add(now.UtcDateTime); index++; }
        }
        if (cursor is { } decoded)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND (o.journal_day,o.id)<(${index},${index + 1})");
            values.Add(decoded.JournalDay); values.Add(decoded.Id); index += 2;
        }
        sql.Append(CultureInfo.InvariantCulture, $" ORDER BY o.journal_day DESC,o.id DESC LIMIT ${index}");
        values.Add(limit + 1);

        await using var command = db.CreateCommand(sql.ToString());
        foreach (var value in values) command.Parameters.Add(Parameter(value));
        var observations = new List<(Guid Id, Guid OwnerId, DateOnly Day)>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) observations.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<DateOnly>(2)));
        var hasMore = observations.Count > limit;
        if (hasMore) observations.RemoveAt(observations.Count - 1);

        var items = new List<GrantedObservationResponse>();
        foreach (var observation in observations)
            items.Add(await ReadClosureAsync(db, agentId, now, observation.Id, observation.OwnerId, observation.Day));
        var next = hasMore ? EncodeCursor(new(CursorVersion, observations[^1].Day, observations[^1].Id)) : null;
        await using var sequenceCommand = db.CreateCommand("SELECT COALESCE(max(sequence),0) FROM journal.record_changes WHERE changed_at<=$1");
        sequenceCommand.Parameters.AddWithValue(now.UtcDateTime);
        var sequence = Convert.ToInt64(await sequenceCommand.ExecuteScalarAsync());
        return new(items, next, EncodeSyncCursor(new(CursorVersion, sequence, now.UtcDateTime)));
    }

    private static async Task<IResult> IncrementalAsync(
        HttpRequest request,
        NpgsqlDataSource db,
        TimeProvider timeProvider,
        string cursor,
        DateOnly? from = null,
        DateOnly? to = null,
        string? subjectType = null,
        string? subject = null,
        Guid? instrumentId = null,
        string? tag = null,
        string? reviewReadiness = null,
        Guid? author = null,
        int limit = 100)
    {
        if (!JournalAccess.TryUser(request, out var agentId)) return Results.Unauthorized();
        if (request.HttpContext.User.FindFirst("account_type")?.Value != "agent")
            return Results.Problem("agent_required", statusCode: 403);
        if (!TryDecodeSyncCursor(cursor, out var syncCursor))
            return Results.Problem("invalid_cursor", statusCode: 400);
        var now = timeProvider.GetUtcNow();
        if (now - syncCursor!.IssuedAt > TimeSpan.FromDays(90))
            return Results.Problem("cursor_expired_fresh_sync_required", statusCode: 410);
        if (limit is < 1 or > 100 || from is { } start && to is { } end && start > end)
            return Results.Problem("invalid_filter", statusCode: 400);

        ObservationSubjectType? subjectFilter = null;
        if (subjectType is not null)
        {
            if (!Enum.TryParse<ObservationSubjectType>(subjectType, out var parsed)
                || parsed == ObservationSubjectType.instrument || string.IsNullOrWhiteSpace(subject))
                return Results.Problem("invalid_filter", statusCode: 400);
            subjectFilter = parsed;
        }
        else if (!string.IsNullOrWhiteSpace(subject))
            return Results.Problem("invalid_filter", statusCode: 400);
        ExpectationReadiness? readinessFilter = null;
        if (reviewReadiness is not null)
        {
            if (!Enum.TryParse<ExpectationReadiness>(reviewReadiness, out var parsed))
                return Results.Problem("invalid_filter", statusCode: 400);
            readinessFilter = parsed;
        }
        string? normalizedTag = null;
        if (!string.IsNullOrWhiteSpace(tag) && !ObservationTags.TryNormalizeOne(tag, out normalizedTag, out _))
            return Results.Problem("invalid_tag", statusCode: 400);

        var page = await ReadChangesAsync(
            db, agentId, now, syncCursor.Sequence, from, to, subjectFilter, subject?.Trim(),
            instrumentId, normalizedTag, readinessFilter, author, limit);
        return Results.Ok(page);
    }

    private static async Task<GrantedChangePage> ReadChangesAsync(
        NpgsqlDataSource db,
        Guid agentId,
        DateTimeOffset now,
        long afterSequence,
        DateOnly? from,
        DateOnly? to,
        ObservationSubjectType? subjectType,
        string? subject,
        Guid? instrumentId,
        string? tag,
        ExpectationReadiness? readiness,
        Guid? author,
        int limit)
    {
        var sql = new StringBuilder("""
            WITH eligible AS (
                SELECT c.sequence,c.record_id,c.record_type,c.owner_user_id,c.market_observation_id,c.operation,c.changed_at,o.journal_day
                FROM journal.record_changes c
                JOIN journal.market_observations o ON o.id=c.market_observation_id AND o.user_id=c.owner_user_id
                WHERE c.sequence>$1
                  AND EXISTS (
                    SELECT 1 FROM journal.agent_access_grants g
                    WHERE g.agent_user_id=$2 AND g.owner_user_id=c.owner_user_id
                      AND g.revoked_at IS NULL AND (g.expires_at IS NULL OR g.expires_at>$3)
                      AND o.journal_day BETWEEN g.from_date AND g.to_date
                      AND (
                        (g.mode='fixed' AND EXISTS (
                            SELECT 1 FROM journal.agent_access_grant_records gr
                            WHERE gr.grant_id=g.id AND gr.record_type=c.record_type AND gr.record_id=c.record_id
                        ))
                        OR
                        (g.mode='ongoing' AND (
                            (g.subject_type IS NULL AND g.instrument_id IS NULL)
                            OR EXISTS (
                                SELECT 1 FROM journal.observation_updates gu
                                WHERE gu.market_observation_id=o.id AND gu.user_id=o.user_id
                                  AND journal.subject_matches(gu.primary_subject,gu.related_subjects,g.subject_type,g.subject_name,g.instrument_id)
                            )
                        ))
                      )
                  )
            """);
        var values = new List<object?> { afterSequence, agentId, now.UtcDateTime };
        var index = 4;
        void Add(string fragment, object value)
        {
            sql.Append(CultureInfo.InvariantCulture, $"{fragment}${index}");
            values.Add(value); index++;
        }
        if (from is { } fromDate) Add(" AND o.journal_day>=", fromDate);
        if (to is { } toDate) Add(" AND o.journal_day<=", toDate);
        if (author is { } authorId) Add(" AND c.owner_user_id=", authorId);
        if (subjectType is { } type && subject is not null)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND EXISTS (SELECT 1 FROM journal.observation_updates fu WHERE fu.market_observation_id=o.id AND fu.user_id=o.user_id AND journal.subject_matches(fu.primary_subject,fu.related_subjects,${index},${index + 1},NULL))");
            values.Add(type.ToString()); values.Add(subject); index += 2;
        }
        if (instrumentId is { } instrument)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND EXISTS (SELECT 1 FROM journal.observation_updates fu WHERE fu.market_observation_id=o.id AND fu.user_id=o.user_id AND journal.subject_matches(fu.primary_subject,fu.related_subjects,NULL,NULL,${index}))");
            values.Add(instrument); index++;
        }
        if (tag is not null)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND EXISTS (SELECT 1 FROM journal.observation_updates fu WHERE fu.market_observation_id=o.id AND fu.user_id=o.user_id AND ${index}=ANY(fu.tags))");
            values.Add(tag); index++;
        }
        if (readiness is { } status)
        {
            var predicate = status switch
            {
                ExpectationReadiness.active => $"e.invalidated_at IS NULL AND e.deadline>${index} AND r.id IS NULL",
                ExpectationReadiness.ready_for_review => $"(e.invalidated_at IS NOT NULL OR e.deadline<=${index}) AND r.id IS NULL",
                _ => "r.id IS NOT NULL AND r.deleted_at IS NULL",
            };
            sql.Append(CultureInfo.InvariantCulture, $" AND EXISTS (SELECT 1 FROM journal.observation_updates ru JOIN journal.expectations e ON e.observation_update_id=ru.id AND e.user_id=ru.user_id LEFT JOIN journal.expectation_reviews r ON r.expectation_id=e.id WHERE ru.market_observation_id=o.id AND ru.user_id=o.user_id AND {predicate})");
            if (status != ExpectationReadiness.reviewed) { values.Add(now.UtcDateTime); index++; }
        }
        sql.Append(CultureInfo.InvariantCulture, $"""
            ), latest AS (
                SELECT DISTINCT ON(record_type,record_id)
                       sequence,record_id,record_type,owner_user_id,market_observation_id,operation,changed_at,journal_day
                FROM eligible
                ORDER BY record_type,record_id,sequence DESC
            )
            SELECT sequence,record_id,record_type,owner_user_id,market_observation_id,operation,changed_at,journal_day
            FROM latest
            ORDER BY sequence
            LIMIT ${index}
            """);
        values.Add(limit + 1);
        await using var command = db.CreateCommand(sql.ToString());
        foreach (var value in values) command.Parameters.Add(Parameter(value));
        var rows = new List<ChangeRow>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                rows.Add(new(
                    reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2), reader.GetGuid(3),
                    reader.GetGuid(4), reader.GetString(5),
                    DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc),
                    reader.GetFieldValue<DateOnly>(7)));
        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var items = new List<JsonElement>();
        foreach (var row in rows)
        {
            JsonElement? content = null;
            var operation = row.Operation;
            if (operation != "deleted")
            {
                var closure = await ReadClosureAsync(db, agentId, now, row.ObservationId, row.OwnerId, row.JournalDay);
                content = closure.Records.FirstOrDefault(record => record.RecordType == row.RecordType && record.Id == row.RecordId)?.Content;
                if (content is null) operation = "deleted";
            }
            items.Add(operation == "deleted"
                ? JsonSerializer.SerializeToElement(new
                {
                    recordId = row.RecordId,
                    recordType = row.RecordType,
                    deletedAt = row.ChangedAt,
                })
                : JsonSerializer.SerializeToElement(new
                {
                    recordId = row.RecordId,
                    recordType = row.RecordType,
                    ownerId = row.OwnerId,
                    operation,
                    changedAt = row.ChangedAt,
                    content,
                }));
        }
        var sequence = rows.Count == 0 ? afterSequence : rows[^1].Sequence;
        return new(items, EncodeSyncCursor(new(CursorVersion, sequence, now.UtcDateTime)), hasMore);
    }

    private static async Task<GrantedObservationResponse> ReadClosureAsync(
        NpgsqlDataSource db, Guid agentId, DateTimeOffset now, Guid observationId, Guid ownerId, DateOnly day)
    {
        await using var command = db.CreateCommand("""
            WITH valid_grants AS (
                SELECT g.id,g.mode
                FROM journal.agent_access_grants g
                WHERE g.agent_user_id=$1 AND g.owner_user_id=$2
                  AND g.revoked_at IS NULL AND (g.expires_at IS NULL OR g.expires_at>$3)
                  AND $4 BETWEEN g.from_date AND g.to_date
                  AND (
                    (g.mode='fixed' AND EXISTS (
                        SELECT 1 FROM journal.agent_access_grant_records gr
                        WHERE gr.grant_id=g.id AND gr.record_type='market_observation' AND gr.record_id=$5
                    ))
                    OR g.mode='ongoing'
                  )
            ),
            records AS (
                SELECT 'market_observation' record_type,o.id,o.updated_at,
                       jsonb_build_object('journalDay',o.journal_day,'timezone',o.timezone,'rollover',to_char(o.rollover_time,'HH24:MI')) content
                FROM journal.market_observations o WHERE o.id=$5 AND o.user_id=$2
                UNION ALL
                SELECT 'observation_update',u.id,u.updated_at,
                       jsonb_build_object('marketObservationId',u.market_observation_id,'content',u.content,'recordedAt',u.recorded_at,
                         'signal',u.signal,'interpretation',u.interpretation,'mentalState',u.mental_state,'tags',u.tags,
                         'primarySubject',u.primary_subject,'relatedSubjects',u.related_subjects,'evidence',u.evidence)
                FROM journal.observation_updates u
                WHERE u.market_observation_id=$5 AND u.user_id=$2 AND u.deleted_at IS NULL
                UNION ALL
                SELECT 'expectation',e.id,e.updated_at,
                       jsonb_build_object('observationUpdateId',e.observation_update_id,'expectedBehavior',e.expected_behavior,'deadline',e.deadline,
                         'invalidationCondition',e.invalidation_condition,'confidence',e.confidence,'market',e.market,'invalidatedAt',e.invalidated_at)
                FROM journal.expectations e JOIN journal.observation_updates u ON u.id=e.observation_update_id AND u.user_id=e.user_id
                WHERE u.market_observation_id=$5 AND e.user_id=$2 AND e.deleted_at IS NULL
                UNION ALL
                SELECT 'expectation_review',r.id,r.updated_at,
                       jsonb_build_object('expectationId',r.expectation_id,'outcome',r.outcome,'reasoningQuality',r.reasoning_quality,'explanation',r.explanation)
                FROM journal.expectation_reviews r
                JOIN journal.expectations e ON e.id=r.expectation_id AND e.user_id=r.user_id
                JOIN journal.observation_updates u ON u.id=e.observation_update_id AND u.user_id=e.user_id
                WHERE u.market_observation_id=$5 AND r.user_id=$2 AND r.deleted_at IS NULL
                UNION ALL
                SELECT 'action_decision',a.id,a.updated_at,
                       jsonb_build_object('observationUpdateId',a.observation_update_id,'expectationId',a.expectation_id,'intent',a.intent,
                         'reason',a.reason,'recordedAt',a.recorded_at,'executionReview',a.execution_review)
                FROM journal.action_decisions a JOIN journal.observation_updates u ON u.id=a.observation_update_id AND u.user_id=a.user_id
                WHERE u.market_observation_id=$5 AND a.user_id=$2 AND a.deleted_at IS NULL
                UNION ALL
                SELECT 'trade',t.id,t.updated_at,
                       jsonb_build_object('actionDecisionId',t.action_decision_id,'symbol',t.symbol,'side',t.side,'quantity',t.quantity,
                         'price',t.price,'currency',t.currency,'executedAt',t.executed_at,'note',t.note)
                FROM journal.trades t
                JOIN journal.action_decisions a ON a.id=t.action_decision_id AND a.user_id=t.user_id
                JOIN journal.observation_updates u ON u.id=a.observation_update_id AND u.user_id=a.user_id
                WHERE u.market_observation_id=$5 AND t.user_id=$2 AND t.deleted_at IS NULL
            )
            SELECT r.record_type,r.id,r.updated_at,r.content::text
            FROM records r
            WHERE EXISTS (
                SELECT 1 FROM valid_grants g
                WHERE g.mode='ongoing' OR EXISTS (
                    SELECT 1 FROM journal.agent_access_grant_records gr
                    WHERE gr.grant_id=g.id AND gr.record_type=r.record_type AND gr.record_id=r.id
                )
            )
            ORDER BY CASE r.record_type
                WHEN 'market_observation' THEN 0 WHEN 'observation_update' THEN 1 WHEN 'expectation' THEN 2
                WHEN 'expectation_review' THEN 3 WHEN 'action_decision' THEN 4 ELSE 5 END,r.id
            """);
        command.Parameters.AddWithValue(agentId);
        command.Parameters.AddWithValue(ownerId);
        command.Parameters.AddWithValue(now.UtcDateTime);
        command.Parameters.AddWithValue(day);
        command.Parameters.AddWithValue(observationId);
        var records = new List<GrantedRecordResponse>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            records.Add(new(reader.GetString(0), reader.GetGuid(1), ownerId,
                DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc),
                JsonSerializer.Deserialize<JsonElement>(reader.GetString(3))));
        return new(observationId, ownerId, day, records);
    }

    private static async Task CaptureFixedClosureAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, Guid grantId, Guid ownerId, AccessGrantWrite input)
    {
        await using var command = new NpgsqlCommand("""
            WITH matching_observations AS (
                SELECT DISTINCT o.id
                FROM journal.market_observations o
                WHERE o.user_id=$2 AND o.deleted_at IS NULL AND o.journal_day BETWEEN $3 AND $4
                  AND (
                    ($5::text IS NULL AND $7::uuid IS NULL)
                    OR EXISTS (
                        SELECT 1 FROM journal.observation_updates u
                        WHERE u.market_observation_id=o.id AND u.user_id=o.user_id AND u.deleted_at IS NULL
                          AND journal.subject_matches(u.primary_subject,u.related_subjects,$5,$6,$7)
                    )
                  )
            ),
            closure(record_type,record_id) AS (
                SELECT 'market_observation',id FROM matching_observations
                UNION ALL
                SELECT 'observation_update',u.id FROM journal.observation_updates u JOIN matching_observations o ON o.id=u.market_observation_id WHERE u.user_id=$2 AND u.deleted_at IS NULL
                UNION ALL
                SELECT 'expectation',e.id FROM journal.expectations e JOIN journal.observation_updates u ON u.id=e.observation_update_id AND u.user_id=e.user_id JOIN matching_observations o ON o.id=u.market_observation_id WHERE e.user_id=$2 AND e.deleted_at IS NULL
                UNION ALL
                SELECT 'expectation_review',r.id FROM journal.expectation_reviews r JOIN journal.expectations e ON e.id=r.expectation_id AND e.user_id=r.user_id JOIN journal.observation_updates u ON u.id=e.observation_update_id AND u.user_id=e.user_id JOIN matching_observations o ON o.id=u.market_observation_id WHERE r.user_id=$2 AND r.deleted_at IS NULL
                UNION ALL
                SELECT 'action_decision',a.id FROM journal.action_decisions a JOIN journal.observation_updates u ON u.id=a.observation_update_id AND u.user_id=a.user_id JOIN matching_observations o ON o.id=u.market_observation_id WHERE a.user_id=$2 AND a.deleted_at IS NULL
                UNION ALL
                SELECT 'trade',t.id FROM journal.trades t JOIN journal.action_decisions a ON a.id=t.action_decision_id AND a.user_id=t.user_id JOIN journal.observation_updates u ON u.id=a.observation_update_id AND u.user_id=a.user_id JOIN matching_observations o ON o.id=u.market_observation_id WHERE t.user_id=$2 AND t.deleted_at IS NULL
            )
            INSERT INTO journal.agent_access_grant_records(grant_id,record_type,record_id)
            SELECT $1,record_type,record_id FROM closure
            """, connection, tx);
        command.Parameters.AddWithValue(grantId);
        command.Parameters.AddWithValue(ownerId);
        command.Parameters.AddWithValue(input.From);
        command.Parameters.AddWithValue(input.To);
        command.Parameters.AddWithValue((object?)input.SubjectType ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)input.Subject?.Trim() ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)input.InstrumentId ?? DBNull.Value });
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> IsManagedAgentAsync(
        IHttpClientFactory clients, IConfiguration configuration, Guid ownerId, Guid agentId, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"/internal/auth/agents/{agentId}/managed-by/{ownerId}");
        message.Headers.Add("X-Service-Key", configuration["Internal:ServiceKey"] ?? "");
        try
        {
            using var response = await clients.CreateClient("identity").SendAsync(message, cancellationToken);
            return response.StatusCode == HttpStatusCode.NoContent;
        }
        catch (HttpRequestException) { return false; }
    }

    private static string? Validate(AccessGrantWrite input, DateTimeOffset now)
    {
        if (input.AgentUserId == Guid.Empty || input.From > input.To || input.To.DayNumber - input.From.DayNumber > 3660)
            return "invalid_grant";
        if ((input.SubjectType is null) != string.IsNullOrWhiteSpace(input.Subject)
            || (input.SubjectType is not null
                && (!Enum.TryParse<ObservationSubjectType>(input.SubjectType, out var type) || type == ObservationSubjectType.instrument))
            || input.Subject?.Trim().Length > 120)
            return "invalid_subject";
        if (input.SubjectType is not null && input.InstrumentId is not null) return "invalid_subject";
        if (input.ExpiresAt is { } expiry && expiry <= now) return "invalid_expiry";
        return null;
    }

    private static void AddGrantParameters(NpgsqlCommand command, Guid id, Guid ownerId, AccessGrantWrite input)
    {
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(ownerId);
        command.Parameters.AddWithValue(input.AgentUserId);
        command.Parameters.AddWithValue(input.Mode.ToString());
        command.Parameters.AddWithValue(input.From);
        command.Parameters.AddWithValue(input.To);
        command.Parameters.AddWithValue((object?)input.SubjectType ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)input.Subject?.Trim() ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)input.InstrumentId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = (object?)input.ExpiresAt?.UtcDateTime ?? DBNull.Value });
    }

    private static AccessGrantResponse ReadGrant(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), Enum.Parse<AccessGrantMode>(reader.GetString(2)),
        reader.GetFieldValue<DateOnly>(3), reader.GetFieldValue<DateOnly>(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetGuid(7),
        reader.IsDBNull(8) ? null : DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc),
        reader.IsDBNull(9) ? null : DateTime.SpecifyKind(reader.GetDateTime(9), DateTimeKind.Utc),
        DateTime.SpecifyKind(reader.GetDateTime(10), DateTimeKind.Utc));

    private static NpgsqlParameter Parameter(object? value) => value switch
    {
        DateOnly date => new() { NpgsqlDbType = NpgsqlDbType.Date, Value = date },
        DateTime dateTime => new() { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc) },
        Guid id => new() { NpgsqlDbType = NpgsqlDbType.Uuid, Value = id },
        int number => new() { NpgsqlDbType = NpgsqlDbType.Integer, Value = number },
        _ => new() { Value = value ?? DBNull.Value },
    };

    private static string EncodeCursor(GrantedCursor cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor, CursorJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecodeCursor(string value, out GrantedCursor? cursor)
    {
        cursor = null;
        try
        {
            var encoded = value.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            cursor = JsonSerializer.Deserialize<GrantedCursor>(Convert.FromBase64String(encoded), CursorJson);
            return cursor is { Version: CursorVersion } parsed && parsed.JournalDay != default && parsed.Id != Guid.Empty;
        }
        catch (Exception error) when (error is FormatException or JsonException) { return false; }
    }

    private static string EncodeSyncCursor(IncrementalCursor cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor, CursorJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecodeSyncCursor(string value, out IncrementalCursor? cursor)
    {
        cursor = null;
        try
        {
            var encoded = value.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            cursor = JsonSerializer.Deserialize<IncrementalCursor>(Convert.FromBase64String(encoded), CursorJson);
            return cursor is { Version: CursorVersion, Sequence: >= 0 } parsed && parsed.IssuedAt != default;
        }
        catch (Exception error) when (error is FormatException or JsonException) { return false; }
    }
}

enum AccessGrantMode { @fixed, ongoing }
record AccessGrantWrite(
    Guid AgentUserId,
    AccessGrantMode Mode,
    DateOnly From,
    DateOnly To,
    string? SubjectType = null,
    string? Subject = null,
    Guid? InstrumentId = null,
    DateTimeOffset? ExpiresAt = null);
record AccessGrantResponse(
    Guid Id,
    Guid AgentUserId,
    AccessGrantMode Mode,
    DateOnly From,
    DateOnly To,
    string? SubjectType,
    string? Subject,
    Guid? InstrumentId,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    DateTime CreatedAt);
record GrantedRecordResponse(string RecordType, Guid Id, Guid OwnerId, DateTime UpdatedAt, JsonElement Content);
record GrantedObservationResponse(Guid MarketObservationId, Guid OwnerId, DateOnly JournalDay, IReadOnlyList<GrantedRecordResponse> Records);
record GrantedRecordPage(IReadOnlyList<GrantedObservationResponse> Items, string? NextCursor, string SyncCursor);
record GrantedCursor(int Version, DateOnly JournalDay, Guid Id);
record IncrementalCursor(int Version, long Sequence, DateTimeOffset IssuedAt);
record ChangeRow(long Sequence, Guid RecordId, string RecordType, Guid OwnerId, Guid ObservationId, string Operation, DateTime ChangedAt, DateOnly JournalDay);
record GrantedChangePage(IReadOnlyList<JsonElement> Items, string NextCursor, bool HasMore);
