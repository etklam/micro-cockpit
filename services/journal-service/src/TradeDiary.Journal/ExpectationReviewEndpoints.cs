using Npgsql;
using NpgsqlTypes;

static class ExpectationReviewEndpoints
{
    private const int MaxExplanationLength = 2000;
    internal static readonly IReadOnlyDictionary<string, string> SystemIssues = new Dictionary<string, string>
    {
        ["insufficient_evidence"] = "Insufficient evidence",
        ["contrary_evidence_ignored"] = "Contrary evidence ignored",
        ["unsupported_inference"] = "Unsupported inference",
        ["unsuitable_observation_horizon"] = "Unsuitable observation horizon",
        ["unclear_invalidation_condition"] = "Unclear invalidation condition",
        ["confidence_miscalibration"] = "Confidence miscalibration",
    };
    internal static readonly IReadOnlyDictionary<string, string> SystemStrengths = new Dictionary<string, string>
    {
        ["sufficient_evidence"] = "Sufficient evidence",
        ["contrary_evidence_considered"] = "Contrary evidence considered",
        ["clear_reasoning_chain"] = "Clear reasoning chain",
        ["suitable_observation_horizon"] = "Suitable observation horizon",
        ["clear_invalidation_condition"] = "Clear invalidation condition",
        ["proportionate_confidence"] = "Proportionate confidence",
    };

    internal static void Map(RouteGroupBuilder journal)
    {
        journal.MapGet("/reasoning-labels", async (HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var labels = Defaults().ToList();
            await using var command = db.CreateCommand("""
                SELECT id,kind,name FROM journal.reasoning_labels
                WHERE user_id=$1 AND deleted_at IS NULL ORDER BY kind,lower(name),id
                """);
            command.Parameters.AddWithValue(userId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetGuid(0);
                labels.Add(new(id, Enum.Parse<ReasoningLabelKind>(reader.GetString(1)), id.ToString(), reader.GetString(2), false));
            }
            return Results.Ok(new CollectionResponse<ReasoningLabelResponse>(labels));
        }).Produces<CollectionResponse<ReasoningLabelResponse>>(200).ProducesProblem(401);

        journal.MapPost("/reasoning-labels", async (ReasoningLabelWrite input, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var name = input.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100) return Results.Problem("invalid_label_name", statusCode: 400);
            var id = Guid.NewGuid();
            try
            {
                await using var command = db.CreateCommand("""
                    INSERT INTO journal.reasoning_labels(id,user_id,kind,name) VALUES($1,$2,$3,$4)
                    RETURNING created_at
                    """);
                command.Parameters.AddWithValue(id);
                command.Parameters.AddWithValue(userId);
                command.Parameters.AddWithValue(input.Kind.ToString());
                command.Parameters.AddWithValue(name);
                await command.ExecuteScalarAsync();
            }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Results.Problem("duplicate_label", statusCode: 409);
            }
            return Results.Created($"/internal/reasoning-labels/{id}", new ReasoningLabelResponse(id, input.Kind, id.ToString(), name, false));
        }).Produces<ReasoningLabelResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(409);

        journal.MapPut("/reasoning-labels/{id:guid}", async (Guid id, ReasoningLabelWrite input, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var name = input.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100) return Results.Problem("invalid_label_name", statusCode: 400);
            try
            {
                await using var command = db.CreateCommand("""
                    UPDATE journal.reasoning_labels SET name=$4,updated_at=now()
                    WHERE id=$1 AND user_id=$2 AND kind=$3 AND deleted_at IS NULL
                    """);
                command.Parameters.AddWithValue(id);
                command.Parameters.AddWithValue(userId);
                command.Parameters.AddWithValue(input.Kind.ToString());
                command.Parameters.AddWithValue(name);
                if (await command.ExecuteNonQueryAsync() == 0) return Results.Problem("not_found", statusCode: 404);
            }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Results.Problem("duplicate_label", statusCode: 409);
            }
            return Results.Ok(new ReasoningLabelResponse(id, input.Kind, id.ToString(), name, false));
        }).Produces<ReasoningLabelResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        journal.MapDelete("/reasoning-labels/{id:guid}", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var connection = await db.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using (var confirmed = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM journal.confirmed_patterns WHERE custom_label_id=$1 AND user_id=$2)", connection, tx))
            {
                confirmed.Parameters.AddWithValue(id);
                confirmed.Parameters.AddWithValue(userId);
                if ((bool)(await confirmed.ExecuteScalarAsync())!)
                    return Results.Problem("confirmed_pattern_in_use", statusCode: 409);
            }
            await using (var links = new NpgsqlCommand("DELETE FROM journal.expectation_review_labels WHERE custom_label_id=$1 AND user_id=$2", connection, tx))
            { links.Parameters.AddWithValue(id); links.Parameters.AddWithValue(userId); await links.ExecuteNonQueryAsync(); }
            await using var command = new NpgsqlCommand("DELETE FROM journal.reasoning_labels WHERE id=$1 AND user_id=$2", connection, tx);
            command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId);
            if (await command.ExecuteNonQueryAsync() == 0) return Results.Problem("not_found", statusCode: 404);
            await tx.CommitAsync();
            return Results.NoContent();
        }).Produces(204).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        journal.MapGet("/expectations/{id:guid}/review", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var review = await ReadReview(db, id, userId);
            return review is null ? Results.Problem("not_found", statusCode: 404) : Results.Ok(review);
        }).Produces<ExpectationReviewResponse>(200).ProducesProblem(401).ProducesProblem(404);

        journal.MapGet("/expectations/{id:guid}/review-context", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var connection = await db.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);

            Guid updateId;
            await using (var expectation = new NpgsqlCommand("""
                SELECT observation_update_id FROM journal.expectations
                WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL
                """, connection, tx))
            {
                expectation.Parameters.AddWithValue(id);
                expectation.Parameters.AddWithValue(userId);
                var value = await expectation.ExecuteScalarAsync();
                if (value is not Guid sourceId) return Results.Problem("not_found", statusCode: 404);
                updateId = sourceId;
            }

            ReviewContextObservationResponse? observation = null;
            ObservationUpdateResponse? update = null;
            await using (var source = new NpgsqlCommand("""
                SELECT o.id,o.journal_day,
                       u.id,u.content,u.recorded_at,u.updated_at,u.signal,u.interpretation,u.mental_state,u.tags,
                       u.primary_subject::text,u.related_subjects::text,u.evidence::text
                FROM journal.observation_updates u
                LEFT JOIN journal.market_observations o
                  ON o.id=u.market_observation_id AND o.user_id=u.user_id AND o.deleted_at IS NULL
                WHERE u.id=$1 AND u.user_id=$2 AND u.deleted_at IS NULL
                """, connection, tx))
            {
                source.Parameters.AddWithValue(updateId);
                source.Parameters.AddWithValue(userId);
                await using var reader = await source.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0)) observation = new(reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1));
                    update = ObservationEnrichment.Read(reader, 2);
                }
            }

            var unavailable = new List<string>();
            if (update is null) unavailable.Add("observation_update");
            if (observation is null) unavailable.Add("market_observation");
            var decisions = new List<ReviewContextActionDecisionResponse>();
            if (update is not null)
            {
                var grouped = new Dictionary<Guid, (ActionDecisionResponse Decision, List<TradeEvidenceResponse> Trades)>();
                await using var evidence = new NpgsqlCommand("""
                    SELECT d.id,d.observation_update_id,d.expectation_id,d.intent,d.reason,d.recorded_at,d.execution_review,d.updated_at,
                           t.id,t.action_decision_id,t.symbol,t.side,t.quantity,t.price,t.currency,t.executed_at,t.note,t.created_at,t.updated_at
                    FROM journal.action_decisions d
                    LEFT JOIN journal.trades t
                      ON t.action_decision_id=d.id AND t.user_id=d.user_id AND t.deleted_at IS NULL
                    WHERE d.observation_update_id=$1 AND d.user_id=$2 AND d.deleted_at IS NULL
                    ORDER BY d.recorded_at,d.id,t.executed_at,t.id
                    """, connection, tx);
                evidence.Parameters.AddWithValue(updateId);
                evidence.Parameters.AddWithValue(userId);
                await using var reader = await evidence.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var decisionId = reader.GetGuid(0);
                    if (!grouped.TryGetValue(decisionId, out var context))
                    {
                        context = (ActionDecisionEndpoints.ReadDecision(reader), []);
                        grouped.Add(decisionId, context);
                    }
                    if (!reader.IsDBNull(8)) context.Trades.Add(ActionDecisionEndpoints.ReadTrade(reader, 8));
                }
                decisions.AddRange(grouped.Values.Select(item => new ReviewContextActionDecisionResponse(item.Decision, item.Trades)));
            }
            else
            {
                unavailable.Add("action_decisions");
                unavailable.Add("trades");
            }

            await tx.CommitAsync();
            return Results.Ok(new ExpectationReviewContextResponse(
                id, updateId,
                unavailable.Count == 0 ? ReviewContextAvailability.available : ReviewContextAvailability.partial,
                unavailable, observation, update, decisions));
        }).Produces<ExpectationReviewContextResponse>(200).ProducesProblem(401).ProducesProblem(404);

        journal.MapDelete("/expectations/{id:guid}/review", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var connection = await db.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using (var confirmed = new NpgsqlCommand("""
                SELECT EXISTS (
                  SELECT 1
                  FROM journal.confirmed_pattern_evidence e
                  JOIN journal.expectation_reviews r ON r.id=e.review_id AND r.user_id=e.user_id
                  WHERE r.expectation_id=$1 AND r.user_id=$2
                )
                """, connection, tx))
            {
                confirmed.Parameters.AddWithValue(id);
                confirmed.Parameters.AddWithValue(userId);
                if ((bool)(await confirmed.ExecuteScalarAsync())!)
                    return Results.Problem("confirmed_pattern_evidence_in_use", statusCode: 409);
            }
            await using (var labels = new NpgsqlCommand("""
                DELETE FROM journal.expectation_review_labels l USING journal.expectation_reviews r
                WHERE l.review_id=r.id AND r.expectation_id=$1 AND r.user_id=$2
                """, connection, tx))
            { labels.Parameters.AddWithValue(id); labels.Parameters.AddWithValue(userId); await labels.ExecuteNonQueryAsync(); }
            await using var command = new NpgsqlCommand("DELETE FROM journal.expectation_reviews WHERE expectation_id=$1 AND user_id=$2", connection, tx);
            command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId);
            if (await command.ExecuteNonQueryAsync() == 0) return Results.Problem("not_found", statusCode: 404);
            await tx.CommitAsync();
            return Results.NoContent();
        }).Produces(204).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        journal.MapPut("/expectations/{id:guid}/review", async (Guid id, ExpectationReviewWrite input, HttpRequest request, NpgsqlDataSource db, TimeProvider timeProvider) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var explanation = string.IsNullOrWhiteSpace(input.Explanation) ? null : input.Explanation.Trim();
            if (explanation?.Length > MaxExplanationLength) return Results.Problem("explanation_too_long", statusCode: 400);
            if (input.Outcome is ExpectationOutcome.partially_confirmed or ExpectationOutcome.indeterminate && explanation is null)
                return Results.Problem("explanation_required", statusCode: 400);

            var issueKeys = (input.SystemIssueKeys ?? []).Distinct(StringComparer.Ordinal).ToArray();
            var strengthKeys = (input.SystemStrengthKeys ?? []).Distinct(StringComparer.Ordinal).ToArray();
            if (issueKeys.Any(key => !SystemIssues.ContainsKey(key)) || strengthKeys.Any(key => !SystemStrengths.ContainsKey(key)))
                return Results.Problem("invalid_system_label", statusCode: 400);
            var customIds = (input.CustomLabelIds ?? []).Distinct().ToArray();

            await using var connection = await db.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using var expectation = new NpgsqlCommand("""
                SELECT deadline,invalidated_at FROM journal.expectations
                WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL FOR UPDATE
                """, connection, tx);
            expectation.Parameters.AddWithValue(id);
            expectation.Parameters.AddWithValue(userId);
            DateTime deadline;
            bool invalidated;
            await using (var reader = await expectation.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync()) return Results.Problem("not_found", statusCode: 404);
                deadline = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
                invalidated = !reader.IsDBNull(1);
            }
            if (!invalidated && deadline > timeProvider.GetUtcNow().UtcDateTime)
                return Results.Problem("expectation_not_ready", statusCode: 409);

            if (customIds.Length > 0)
            {
                await using var labels = new NpgsqlCommand("""
                    SELECT count(*) FROM journal.reasoning_labels
                    WHERE user_id=$1 AND id=ANY($2) AND deleted_at IS NULL
                    """, connection, tx);
                labels.Parameters.AddWithValue(userId);
                labels.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid, Value = customIds });
                if ((long)(await labels.ExecuteScalarAsync())! != customIds.Length)
                    return Results.Problem("invalid_custom_label", statusCode: 400);
            }

            var reviewId = Guid.NewGuid();
            await using (var upsert = new NpgsqlCommand("""
                INSERT INTO journal.expectation_reviews(id,expectation_id,user_id,outcome,reasoning_quality,explanation)
                VALUES($1,$2,$3,$4,$5,$6)
                ON CONFLICT (expectation_id) DO UPDATE SET
                    outcome=excluded.outcome,reasoning_quality=excluded.reasoning_quality,
                    explanation=excluded.explanation,deleted_at=NULL,updated_at=now()
                RETURNING id
                """, connection, tx))
            {
                upsert.Parameters.AddWithValue(reviewId);
                upsert.Parameters.AddWithValue(id);
                upsert.Parameters.AddWithValue(userId);
                upsert.Parameters.AddWithValue(input.Outcome.ToString());
                upsert.Parameters.AddWithValue(input.ReasoningQuality.ToString());
                upsert.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)explanation ?? DBNull.Value });
                reviewId = (Guid)(await upsert.ExecuteScalarAsync())!;
            }
            await using (var clear = new NpgsqlCommand("DELETE FROM journal.expectation_review_labels WHERE review_id=$1", connection, tx))
            {
                clear.Parameters.AddWithValue(reviewId);
                await clear.ExecuteNonQueryAsync();
            }
            foreach (var (kind, systemKey, customId) in issueKeys.Select(key => ("issue", (string?)key, (Guid?)null))
                .Concat(strengthKeys.Select(key => ("strength", (string?)key, (Guid?)null)))
                .Concat(await CustomLabels(connection, tx, userId, customIds)))
            {
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO journal.expectation_review_labels(id,review_id,user_id,kind,system_key,custom_label_id)
                    VALUES($1,$2,$3,$4,$5,$6)
                    """, connection, tx);
                insert.Parameters.AddWithValue(Guid.NewGuid());
                insert.Parameters.AddWithValue(reviewId);
                insert.Parameters.AddWithValue(userId);
                insert.Parameters.AddWithValue(kind);
                insert.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)systemKey ?? DBNull.Value });
                insert.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)customId ?? DBNull.Value });
                await insert.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return Results.Ok((await ReadReview(db, id, userId))!);
        }).Produces<ExpectationReviewResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
    }

    private static IEnumerable<ReasoningLabelResponse> Defaults() =>
        SystemIssues.Select(item => new ReasoningLabelResponse(null, ReasoningLabelKind.issue, item.Key, item.Value, true))
            .Concat(SystemStrengths.Select(item => new ReasoningLabelResponse(null, ReasoningLabelKind.strength, item.Key, item.Value, true)));

    private static async Task<IReadOnlyList<(string kind, string? systemKey, Guid? customId)>> CustomLabels(
        NpgsqlConnection connection, NpgsqlTransaction tx, Guid userId, Guid[] ids)
    {
        var result = new List<(string, string?, Guid?)>();
        if (ids.Length == 0) return result;
        await using var command = new NpgsqlCommand("""
            SELECT id,kind FROM journal.reasoning_labels WHERE user_id=$1 AND id=ANY($2)
            """, connection, tx);
        command.Parameters.AddWithValue(userId);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid, Value = ids });
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add((reader.GetString(1), null, reader.GetGuid(0)));
        return result;
    }

    private static async Task<ExpectationReviewResponse?> ReadReview(NpgsqlDataSource db, Guid expectationId, Guid userId)
    {
        await using var command = db.CreateCommand("""
            SELECT r.id,r.outcome,r.reasoning_quality,r.explanation,r.created_at,r.updated_at,
                   l.kind,l.system_key,l.custom_label_id,c.name
            FROM journal.expectation_reviews r
            LEFT JOIN journal.expectation_review_labels l ON l.review_id=r.id
            LEFT JOIN journal.reasoning_labels c ON c.id=l.custom_label_id
            WHERE r.expectation_id=$1 AND r.user_id=$2 AND r.deleted_at IS NULL
            ORDER BY l.kind,l.system_key,c.name,l.id
            """);
        command.Parameters.AddWithValue(expectationId);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync();
        ExpectationReviewResponse? response = null;
        var labels = new List<ReasoningLabelResponse>();
        while (await reader.ReadAsync())
        {
            response ??= new(
                reader.GetGuid(0), expectationId, Enum.Parse<ExpectationOutcome>(reader.GetString(1)),
                Enum.Parse<ReasoningQuality>(reader.GetString(2)), reader.IsDBNull(3) ? null : reader.GetString(3),
                labels, DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc), DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc));
            if (reader.IsDBNull(6)) continue;
            var kind = Enum.Parse<ReasoningLabelKind>(reader.GetString(6));
            if (!reader.IsDBNull(7))
            {
                var key = reader.GetString(7);
                var names = kind == ReasoningLabelKind.issue ? SystemIssues : SystemStrengths;
                labels.Add(new(null, kind, key, names[key], true));
            }
            else
            {
                var customId = reader.GetGuid(8);
                labels.Add(new(customId, kind, customId.ToString(), reader.GetString(9), false));
            }
        }
        return response;
    }
}
