using Npgsql;
using NpgsqlTypes;

// Expectation lifecycle: a dated, falsifiable claim attached to an observation update.
// Create/list/read/edit/invalidate. First-version domain per CONTEXT.md.
static class ExpectationEndpoints
{
    private const string expectationSelect = """
        SELECT e.id, e.observation_update_id, o.id, o.journal_day,
               e.expected_behavior, e.deadline, e.invalidation_condition, e.confidence, e.market,
               e.invalidated_at, e.created_at, e.updated_at
        FROM journal.expectations e
        JOIN journal.observation_updates u ON u.id=e.observation_update_id AND u.user_id=e.user_id
        JOIN journal.market_observations o ON o.id=u.market_observation_id AND o.user_id=u.user_id
        WHERE e.id=$1 AND e.user_id=$2 AND e.deleted_at IS NULL
        """;

    internal static void Map(RouteGroupBuilder journal)
    {
        journal.MapPost("/observation-updates/{updateId:guid}/expectations", async (Guid updateId, ExpectationWrite input, HttpRequest request, NpgsqlDataSource db, TimeProvider timeProvider) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var now = timeProvider.GetUtcNow();
            var error = ExpectationRules.Validate(input, now, out var normalized);
            if (error is not null) return Results.Problem(error, statusCode: 400);
            if (!JournalAccess.TryIdempotencyKey(request, out var key)) return Results.Problem("invalid_idempotency_key", statusCode: 400);
            var result = await JournalAccess.Idempotent(db, userId, $"create-expectation:{updateId}", key, input, async (connection, tx) =>
            {
                var id = Guid.NewGuid();
                await using var command = new NpgsqlCommand("""
                    WITH parent AS (
                      SELECT u.id AS update_id, o.id AS observation_id, o.journal_day
                      FROM journal.observation_updates u
                      JOIN journal.market_observations o ON o.id=u.market_observation_id AND o.user_id=u.user_id AND o.deleted_at IS NULL
                      WHERE u.id=$8 AND u.user_id=$2 AND u.deleted_at IS NULL
                    ), inserted AS (
                      INSERT INTO journal.expectations (id, observation_update_id, user_id, expected_behavior, deadline, invalidation_condition, confidence, market)
                      SELECT $1, parent.update_id, $2, $3, $4, $5, $6, $7 FROM parent
                      RETURNING id, observation_update_id, expected_behavior, deadline, invalidation_condition, confidence, market, invalidated_at, created_at, updated_at
                    )
                    SELECT inserted.id, inserted.observation_update_id, parent.observation_id, parent.journal_day,
                           inserted.expected_behavior, inserted.deadline, inserted.invalidation_condition, inserted.confidence, inserted.market,
                           inserted.invalidated_at, inserted.created_at, inserted.updated_at
                    FROM inserted JOIN parent ON parent.update_id = inserted.observation_update_id
                    """, connection, tx);
                command.Parameters.AddWithValue(id);
                command.Parameters.AddWithValue(userId);
                command.Parameters.AddWithValue(normalized.ExpectedBehavior);
                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = normalized.Deadline.UtcDateTime });
                command.Parameters.AddWithValue(normalized.InvalidationCondition);
                command.Parameters.AddWithValue(normalized.Confidence.ToString());
                command.Parameters.AddWithValue(normalized.Market);
                command.Parameters.AddWithValue(updateId);
                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return JournalAccess.Stored(404, null, new { error = "not_found" });
                var response = ExpectationRules.Read(reader, now);
                return JournalAccess.Stored(201, $"/internal/expectations/{response.Id}", response);
            });
            return JournalAccess.WriteResult(request.HttpContext, result);
        })
        .Produces<ExpectationResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409)
        .WithMetadata(new IdempotencyKeyHeaderMarker());

        journal.MapGet("/expectations", async (HttpRequest request, NpgsqlDataSource db, TimeProvider timeProvider, Guid? observationUpdateId = null) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var command = db.CreateCommand("""
                SELECT e.id, e.observation_update_id, o.id, o.journal_day,
                       e.expected_behavior, e.deadline, e.invalidation_condition, e.confidence, e.market,
                       e.invalidated_at, e.created_at, e.updated_at
                FROM journal.expectations e
                JOIN journal.observation_updates u ON u.id=e.observation_update_id AND u.user_id=e.user_id
                JOIN journal.market_observations o ON o.id=u.market_observation_id AND o.user_id=u.user_id
                WHERE e.user_id=$1 AND e.deleted_at IS NULL AND ($2::uuid IS NULL OR e.observation_update_id=$2)
                ORDER BY e.created_at DESC
                """);
            command.Parameters.AddWithValue(userId);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)observationUpdateId ?? DBNull.Value });
            var now = timeProvider.GetUtcNow();
            var items = new List<ExpectationResponse>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) items.Add(ExpectationRules.Read(reader, now));
            return Results.Ok(new CollectionResponse<ExpectationResponse>(items));
        })
        .Produces<CollectionResponse<ExpectationResponse>>(200).ProducesProblem(401);

        journal.MapGet("/expectations/{id:guid}", async (Guid id, HttpRequest request, NpgsqlDataSource db, TimeProvider timeProvider) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var command = db.CreateCommand(expectationSelect);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(userId);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Results.Ok(ExpectationRules.Read(reader, timeProvider.GetUtcNow())) : Results.Problem("not_found", statusCode: 404);
        })
        .Produces<ExpectationResponse>(200).ProducesProblem(401).ProducesProblem(404);

        journal.MapPut("/expectations/{id:guid}", async (Guid id, ExpectationWrite input, HttpRequest request, NpgsqlDataSource db, TimeProvider timeProvider) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var now = timeProvider.GetUtcNow();
            var error = ExpectationRules.Validate(input, now, out var normalized);
            if (error is not null) return Results.Problem(error, statusCode: 400);

            await using var existing = db.CreateCommand("SELECT deadline FROM journal.expectations WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL");
            existing.Parameters.AddWithValue(id);
            existing.Parameters.AddWithValue(userId);
            DateTime oldDeadline;
            await using (var existingReader = await existing.ExecuteReaderAsync())
            {
                if (!await existingReader.ReadAsync()) return Results.Problem("not_found", statusCode: 404);
                oldDeadline = DateTime.SpecifyKind(existingReader.GetDateTime(0), DateTimeKind.Utc);
            }

            await using var update = db.CreateCommand("""
                UPDATE journal.expectations
                SET expected_behavior=$3, deadline=$4, invalidation_condition=$5, confidence=$6, market=$7, updated_at=now()
                WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL
                """);
            update.Parameters.AddWithValue(id);
            update.Parameters.AddWithValue(userId);
            update.Parameters.AddWithValue(normalized.ExpectedBehavior);
            update.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = normalized.Deadline.UtcDateTime });
            update.Parameters.AddWithValue(normalized.InvalidationCondition);
            update.Parameters.AddWithValue(normalized.Confidence.ToString());
            update.Parameters.AddWithValue(normalized.Market);
            await update.ExecuteNonQueryAsync();

            await using var command = db.CreateCommand(expectationSelect);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(userId);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            var response = ExpectationRules.Read(reader, now);
            return Results.Ok(ExpectationRules.Edit(response, oldDeadline <= now.UtcDateTime));
        })
        .Produces<ExpectationEditResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

        journal.MapPost("/expectations/{id:guid}/invalidate", async (Guid id, HttpRequest request, NpgsqlDataSource db, TimeProvider timeProvider) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var update = db.CreateCommand("""
                UPDATE journal.expectations SET invalidated_at=coalesce(invalidated_at, now()), updated_at=now()
                WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL
                """);
            update.Parameters.AddWithValue(id);
            update.Parameters.AddWithValue(userId);
            if (await update.ExecuteNonQueryAsync() == 0) return Results.Problem("not_found", statusCode: 404);

            await using var command = db.CreateCommand(expectationSelect);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(userId);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            return Results.Ok(ExpectationRules.Read(reader, timeProvider.GetUtcNow()));
        })
        .Produces<ExpectationResponse>(200).ProducesProblem(401).ProducesProblem(404);
    }
}
