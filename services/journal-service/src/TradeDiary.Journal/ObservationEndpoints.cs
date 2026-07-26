using Npgsql;
using NpgsqlTypes;

// Market Observation surface: Quick Observation capture, the day workspace, progressive
// enrichment, and observation search/summary. First-version domain per CONTEXT.md.
static class ObservationEndpoints
{
    internal static void Map(RouteGroupBuilder journal)
    {
        journal.MapGet("/market-observations", async (
            HttpRequest request,
            NpgsqlDataSource db,
            string? query = null,
            DateOnly? from = null,
            DateOnly? to = null,
            ObservationSubjectType? subjectType = null,
            string? subject = null,
            Guid? instrumentId = null,
            string? market = null,
            string? symbol = null,
            string? tag = null,
            string? author = null,
            string? cursor = null,
            int limit = 20) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var error = ObservationQuery.Validate(query, from, to, subjectType, subject, instrumentId, market, symbol, tag, author, limit, cursor, out var parsed);
            if (error is not null) return Results.Problem(error, statusCode: 400);
            return Results.Ok(await ObservationQuery.ReadAsync(db, userId, parsed));
        })
        .Produces<ObservationSearchPage>(200).ProducesProblem(400).ProducesProblem(401);

        journal.MapGet("/market-observation-day-summary", async (DateOnly from, DateOnly to, HttpRequest request, NpgsqlDataSource db, TimeProvider timeProvider) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            if (to < from || to.DayNumber - from.DayNumber > 62) return Results.Problem("invalid_date_range", statusCode: 400);
            await using var command = db.CreateCommand("""
                SELECT o.journal_day,o.id,count(DISTINCT u.id),
                       count(DISTINCT e.id) FILTER (WHERE e.invalidated_at IS NOT NULL OR e.deadline <= $4)
                FROM journal.market_observations o
                JOIN journal.observation_updates u ON u.market_observation_id=o.id AND u.user_id=o.user_id AND u.deleted_at IS NULL
                LEFT JOIN journal.expectations e ON e.observation_update_id=u.id AND e.user_id=u.user_id AND e.deleted_at IS NULL
                WHERE o.user_id=$1 AND o.deleted_at IS NULL AND o.journal_day BETWEEN $2 AND $3
                GROUP BY o.journal_day,o.id ORDER BY o.journal_day
                """);
            command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(from); command.Parameters.AddWithValue(to);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = timeProvider.GetUtcNow().UtcDateTime });
            var items = new List<MarketObservationDaySummaryItem>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) items.Add(new(reader.GetFieldValue<DateOnly>(0), reader.GetGuid(1), reader.GetInt64(2), reader.GetInt64(3)));
            return Results.Ok(new CollectionResponse<MarketObservationDaySummaryItem>(items));
        })
        .Produces<CollectionResponse<MarketObservationDaySummaryItem>>(200).ProducesProblem(400).ProducesProblem(401);

        journal.MapGet("/market-observations/today", async (HttpRequest request, NpgsqlDataSource db, TimeProvider timeProvider) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var timezone = request.HttpContext.User.FindFirst("timezone")?.Value ?? "UTC";
            var rollover = request.HttpContext.User.FindFirst("journal_day_rollover")?.Value ?? "00:00";
            DateOnly journalDay;
            try { journalDay = JournalDay.Resolve(timeProvider.GetUtcNow(), timezone, rollover); }
            catch (ArgumentException error) { return Results.Problem(error.Message, statusCode: 400); }

            await using var observation = db.CreateCommand("""
                SELECT id, journal_day, timezone, rollover_time
                FROM journal.market_observations
                WHERE user_id=$1 AND journal_day=$2 AND deleted_at IS NULL
                """);
            observation.Parameters.AddWithValue(userId);
            observation.Parameters.AddWithValue(journalDay);
            Guid observationId;
            DateOnly storedDay;
            string storedTimezone;
            string storedRollover;
            await using (var reader = await observation.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync()) return Results.Problem("not_found", statusCode: 404);
                observationId = reader.GetGuid(0);
                storedDay = reader.GetFieldValue<DateOnly>(1);
                storedTimezone = reader.GetString(2);
                storedRollover = reader.GetFieldValue<TimeOnly>(3).ToString("HH:mm");
            }

            await using var updatesCommand = db.CreateCommand("""
                SELECT id, content, recorded_at, updated_at, signal, interpretation, mental_state, tags,
                       primary_subject::text, related_subjects::text, evidence::text
                FROM journal.observation_updates
                WHERE market_observation_id=$1 AND user_id=$2 AND deleted_at IS NULL
                ORDER BY sequence
                """);
            updatesCommand.Parameters.AddWithValue(observationId);
            updatesCommand.Parameters.AddWithValue(userId);
            var updates = new List<ObservationUpdateResponse>();
            await using (var reader = await updatesCommand.ExecuteReaderAsync())
                while (await reader.ReadAsync()) updates.Add(ObservationEnrichment.Read(reader));
            return Results.Ok(new MarketObservationResponse(observationId, storedDay, storedTimezone, storedRollover, updates));
        })
        .Produces<MarketObservationResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

        journal.MapPost("/quick-observations", async (QuickObservationWrite input, HttpRequest request, NpgsqlDataSource db, TimeProvider timeProvider) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(input.Content)) return Results.Problem("content_required", statusCode: 400);
            if (!JournalAccess.TryIdempotencyKey(request, out var key)) return Results.Problem("invalid_idempotency_key", statusCode: 400);
            var timezone = request.HttpContext.User.FindFirst("timezone")?.Value ?? "UTC";
            var rollover = request.HttpContext.User.FindFirst("journal_day_rollover")?.Value ?? "00:00";
            var recordedAt = timeProvider.GetUtcNow();
            DateOnly journalDay;
            TimeOnly rolloverTime;
            try
            {
                journalDay = JournalDay.Resolve(recordedAt, timezone, rollover);
                rolloverTime = TimeOnly.ParseExact(rollover, "HH:mm");
            }
            catch (ArgumentException error) { return Results.Problem(error.Message, statusCode: 400); }
            catch (FormatException) { return Results.Problem("invalid_rollover", statusCode: 400); }

            var result = await JournalAccess.Idempotent(db, userId, "quick-observation", key, input, async (connection, tx) =>
            {
                var proposedId = Guid.NewGuid();
                await using var createObservation = new NpgsqlCommand("""
                    INSERT INTO journal.market_observations (id, user_id, journal_day, timezone, rollover_time)
                    VALUES ($1,$2,$3,$4,$5)
                    ON CONFLICT (user_id, journal_day) WHERE deleted_at IS NULL DO NOTHING
                    RETURNING id
                    """, connection, tx);
                createObservation.Parameters.AddWithValue(proposedId);
                createObservation.Parameters.AddWithValue(userId);
                createObservation.Parameters.AddWithValue(journalDay);
                createObservation.Parameters.AddWithValue(timezone);
                createObservation.Parameters.AddWithValue(rolloverTime);
                var inserted = await createObservation.ExecuteScalarAsync();
                var created = inserted is Guid;
                var observationId = created ? (Guid)inserted! : Guid.Empty;
                if (!created)
                {
                    await using var find = new NpgsqlCommand("SELECT id FROM journal.market_observations WHERE user_id=$1 AND journal_day=$2 AND deleted_at IS NULL", connection, tx);
                    find.Parameters.AddWithValue(userId);
                    find.Parameters.AddWithValue(journalDay);
                    observationId = (Guid)(await find.ExecuteScalarAsync() ?? throw new InvalidOperationException("market_observation_missing"));
                }

                var updateId = Guid.NewGuid();
                await using var update = new NpgsqlCommand("""
                    INSERT INTO journal.observation_updates (id, market_observation_id, user_id, content, recorded_at)
                    VALUES ($1,$2,$3,$4,$5)
                    """, connection, tx);
                update.Parameters.AddWithValue(updateId);
                update.Parameters.AddWithValue(observationId);
                update.Parameters.AddWithValue(userId);
                update.Parameters.AddWithValue(input.Content.Trim());
                update.Parameters.AddWithValue(recordedAt.UtcDateTime);
                await update.ExecuteNonQueryAsync();
                await using var touch = new NpgsqlCommand("UPDATE journal.market_observations SET updated_at=now() WHERE id=$1 AND user_id=$2", connection, tx);
                touch.Parameters.AddWithValue(observationId);
                touch.Parameters.AddWithValue(userId);
                await touch.ExecuteNonQueryAsync();
                var body = new QuickObservationResponse(observationId, updateId, journalDay, recordedAt.UtcDateTime, !created);
                return JournalAccess.Stored(created ? 201 : 200, null, body);
            });
            return JournalAccess.WriteResult(request.HttpContext, result);
        })
        .Produces<QuickObservationResponse>(200).Produces<QuickObservationResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(409)
        .WithMetadata(new IdempotencyKeyHeaderMarker());

        journal.MapPut("/observation-updates/{id:guid}", async (Guid id, ObservationUpdateWrite input, HttpRequest request, NpgsqlDataSource db, IHttpClientFactory httpFactory) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var error = ObservationEnrichment.Normalize(input, out var value);
            if (error is not null) return Results.Problem(error, statusCode: 400);
            var resolution = await ObservationInstruments.ResolveAsync(httpFactory, value, request.HttpContext.RequestAborted);
            if (resolution.Error is not null) return Results.Problem(resolution.Error, statusCode: resolution.StatusCode);
            value = resolution.Value!;
            await using var command = db.CreateCommand("""
                UPDATE journal.observation_updates u
                SET content=$3, signal=$4, interpretation=$5, mental_state=$6, tags=$7,
                    primary_subject=$8, related_subjects=$9, evidence=$10, updated_at=now()
                FROM journal.market_observations o
                WHERE u.id=$1 AND u.user_id=$2 AND u.deleted_at IS NULL
                  AND o.id=u.market_observation_id AND o.user_id=$2 AND o.deleted_at IS NULL
                RETURNING u.id, u.content, u.recorded_at, u.updated_at, u.signal, u.interpretation,
                          u.mental_state, u.tags, u.primary_subject::text, u.related_subjects::text, u.evidence::text
                """);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(value.Content);
            command.Parameters.AddWithValue((object?)value.Signal ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)value.Interpretation ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)value.MentalState ?? DBNull.Value);
            command.Parameters.AddWithValue(value.Tags.ToArray());
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = (object?)ObservationEnrichment.Json(value.PrimarySubject) ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = ObservationEnrichment.Json(value.RelatedSubjects) ?? "[]" });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = (object?)ObservationEnrichment.Json(value.Evidence) ?? DBNull.Value });
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return Results.Problem("not_found", statusCode: 404);
            return Results.Ok(ObservationEnrichment.ReadEdit(reader));
        })
        .Produces<ObservationUpdateEditResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);
    }
}
