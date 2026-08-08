using Npgsql;
using NpgsqlTypes;

static class ActionDecisionEndpoints
{
    private const int MaxTextLength = 2000;

    internal static void Map(RouteGroupBuilder journal)
    {
        journal.MapGet("/observation-updates/{updateId:guid}/action-decisions", async (Guid updateId, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var command = db.CreateCommand("""
                SELECT id,observation_update_id,expectation_id,intent,reason,recorded_at,execution_review,updated_at
                FROM journal.action_decisions
                WHERE observation_update_id=$1 AND user_id=$2 AND deleted_at IS NULL
                ORDER BY recorded_at,id
                """);
            command.Parameters.AddWithValue(updateId);
            command.Parameters.AddWithValue(userId);
            var items = new List<ActionDecisionResponse>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) items.Add(ReadDecision(reader));
            return Results.Ok(new CollectionResponse<ActionDecisionResponse>(items));
        }).Produces<CollectionResponse<ActionDecisionResponse>>(200).ProducesProblem(401);

        journal.MapPost("/observation-updates/{updateId:guid}/action-decisions", async (Guid updateId, ActionDecisionWrite input, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var reason = NormalizeReason(input.Reason);
            if (reason is null) return Results.Problem("invalid_reason", statusCode: 400);
            var id = Guid.NewGuid();
            await using var command = db.CreateCommand("""
                INSERT INTO journal.action_decisions(id,observation_update_id,expectation_id,user_id,intent,reason,execution_review)
                SELECT $1,u.id,e.id,$2,$3,$4,$5
                FROM journal.observation_updates u
                LEFT JOIN journal.expectations e ON e.id=$6 AND e.user_id=u.user_id
                    AND e.observation_update_id=u.id AND e.deleted_at IS NULL
                WHERE u.id=$7 AND u.user_id=$2 AND u.deleted_at IS NULL
                    AND ($6::uuid IS NULL OR e.id IS NOT NULL)
                RETURNING id,observation_update_id,expectation_id,intent,reason,recorded_at,execution_review,updated_at
                """);
            AddDecisionParameters(command, id, userId, input, reason, updateId);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? Results.Created($"/internal/action-decisions/{id}", ReadDecision(reader))
                : Results.Problem("not_found", statusCode: 404);
        }).Produces<ActionDecisionResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

        journal.MapGet("/action-decisions/{id:guid}", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var decision = await FindDecision(db, id, userId);
            return decision is null ? Results.Problem("not_found", statusCode: 404) : Results.Ok(decision);
        }).Produces<ActionDecisionResponse>(200).ProducesProblem(401).ProducesProblem(404);

        journal.MapPut("/action-decisions/{id:guid}", async (Guid id, ActionDecisionWrite input, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var reason = NormalizeReason(input.Reason);
            if (reason is null) return Results.Problem("invalid_reason", statusCode: 400);
            await using var command = db.CreateCommand("""
                UPDATE journal.action_decisions d SET
                    expectation_id=e.id,intent=$3,reason=$4,execution_review=$5,updated_at=now()
                FROM journal.observation_updates u
                LEFT JOIN journal.expectations e ON e.id=$6 AND e.user_id=u.user_id
                    AND e.observation_update_id=u.id AND e.deleted_at IS NULL
                WHERE d.id=$1 AND d.user_id=$2 AND d.deleted_at IS NULL
                    AND u.id=d.observation_update_id AND u.user_id=d.user_id
                    AND ($6::uuid IS NULL OR e.id IS NOT NULL)
                RETURNING d.id,d.observation_update_id,d.expectation_id,d.intent,d.reason,d.recorded_at,d.execution_review,d.updated_at
                """);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(input.Intent.ToString());
            command.Parameters.AddWithValue(reason);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)input.ExecutionReview?.ToString() ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)input.ExpectationId ?? DBNull.Value });
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return Results.Problem("not_found", statusCode: 404);
            var updated = ReadDecision(reader);
            return Results.Ok(new ActionDecisionEditResponse(
                updated.Id, updated.ObservationUpdateId, updated.ExpectationId, updated.Intent, updated.Reason,
                updated.RecordedAt, updated.ExecutionReview, updated.UpdatedAt, true));
        }).Produces<ActionDecisionEditResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

        journal.MapDelete("/action-decisions/{id:guid}", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var connection = await db.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using (var trades = new NpgsqlCommand("""
                DELETE FROM journal.trades
                WHERE action_decision_id=$1 AND user_id=$2
                """, connection, tx))
            {
                trades.Parameters.AddWithValue(id);
                trades.Parameters.AddWithValue(userId);
                await trades.ExecuteNonQueryAsync();
            }
            await using var decision = new NpgsqlCommand("""
                DELETE FROM journal.action_decisions
                WHERE id=$1 AND user_id=$2
                """, connection, tx);
            decision.Parameters.AddWithValue(id);
            decision.Parameters.AddWithValue(userId);
            if (await decision.ExecuteNonQueryAsync() == 0) return Results.Problem("not_found", statusCode: 404);
            await tx.CommitAsync();
            return Results.NoContent();
        }).Produces(204).ProducesProblem(401).ProducesProblem(404);

        journal.MapGet("/action-decisions/{decisionId:guid}/trades", async (Guid decisionId, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var command = db.CreateCommand("""
                SELECT t.id,t.action_decision_id,t.symbol,t.side,t.quantity,t.price,t.currency,t.executed_at,t.note,t.created_at,t.updated_at
                FROM journal.trades t
                JOIN journal.action_decisions d ON d.id=t.action_decision_id AND d.user_id=t.user_id AND d.deleted_at IS NULL
                WHERE t.action_decision_id=$1 AND t.user_id=$2 AND t.deleted_at IS NULL
                ORDER BY t.executed_at,t.id
                """);
            command.Parameters.AddWithValue(decisionId);
            command.Parameters.AddWithValue(userId);
            var items = new List<TradeEvidenceResponse>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) items.Add(ReadTrade(reader));
            return Results.Ok(new CollectionResponse<TradeEvidenceResponse>(items));
        }).Produces<CollectionResponse<TradeEvidenceResponse>>(200).ProducesProblem(401);

        journal.MapPost("/action-decisions/{decisionId:guid}/trades", async (Guid decisionId, TradeEvidenceWrite input, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var error = ValidateTrade(input, out var symbol, out var currency, out var note);
            if (error is not null) return Results.Problem(error, statusCode: 400);
            var id = Guid.NewGuid();
            await using var command = db.CreateCommand("""
                INSERT INTO journal.trades(id,action_decision_id,user_id,symbol,side,quantity,price,currency,executed_at,note)
                SELECT $1,d.id,$2,$3,$4,$5,$6,$7,$8,$9
                FROM journal.action_decisions d
                WHERE d.id=$10 AND d.user_id=$2 AND d.deleted_at IS NULL
                RETURNING id,action_decision_id,symbol,side,quantity,price,currency,executed_at,note,created_at,updated_at
                """);
            AddTradeParameters(command, id, userId, input, symbol, currency, note, decisionId);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? Results.Created($"/internal/action-decisions/{decisionId}/trades/{id}", ReadTrade(reader))
                : Results.Problem("not_found", statusCode: 404);
        }).Produces<TradeEvidenceResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

        journal.MapPut("/action-decisions/{decisionId:guid}/trades/{id:guid}", async (Guid decisionId, Guid id, TradeEvidenceWrite input, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var error = ValidateTrade(input, out var symbol, out var currency, out var note);
            if (error is not null) return Results.Problem(error, statusCode: 400);
            await using var command = db.CreateCommand("""
                UPDATE journal.trades SET symbol=$4,side=$5,quantity=$6,price=$7,currency=$8,executed_at=$9,note=$10,updated_at=now()
                WHERE id=$1 AND action_decision_id=$2 AND user_id=$3 AND deleted_at IS NULL
                RETURNING id,action_decision_id,symbol,side,quantity,price,currency,executed_at,note,created_at,updated_at
                """);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(decisionId);
            command.Parameters.AddWithValue(userId);
            AddTradeValues(command, input, symbol, currency, note);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Results.Ok(ReadTrade(reader)) : Results.Problem("not_found", statusCode: 404);
        }).Produces<TradeEvidenceResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

        journal.MapDelete("/action-decisions/{decisionId:guid}/trades/{id:guid}", async (Guid decisionId, Guid id, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var command = db.CreateCommand("""
                DELETE FROM journal.trades
                WHERE id=$1 AND action_decision_id=$2 AND user_id=$3
                """);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(decisionId);
            command.Parameters.AddWithValue(userId);
            return await command.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
        }).Produces(204).ProducesProblem(401).ProducesProblem(404);
    }

    private static string? NormalizeReason(string? reason)
    {
        var normalized = reason?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > MaxTextLength ? null : normalized;
    }

    private static void AddDecisionParameters(NpgsqlCommand command, Guid id, Guid userId, ActionDecisionWrite input, string reason, Guid updateId)
    {
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(input.Intent.ToString());
        command.Parameters.AddWithValue(reason);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)input.ExecutionReview?.ToString() ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)input.ExpectationId ?? DBNull.Value });
        command.Parameters.AddWithValue(updateId);
    }

    private static async Task<ActionDecisionResponse?> FindDecision(NpgsqlDataSource db, Guid id, Guid userId)
    {
        await using var command = db.CreateCommand("""
            SELECT id,observation_update_id,expectation_id,intent,reason,recorded_at,execution_review,updated_at
            FROM journal.action_decisions WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL
            """);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadDecision(reader) : null;
    }

    internal static ActionDecisionResponse ReadDecision(NpgsqlDataReader reader, int offset = 0) => new(
        reader.GetGuid(offset), reader.GetGuid(offset + 1), reader.IsDBNull(offset + 2) ? null : reader.GetGuid(offset + 2),
        Enum.Parse<ActionDecisionIntent>(reader.GetString(offset + 3)), reader.GetString(offset + 4),
        DateTime.SpecifyKind(reader.GetDateTime(offset + 5), DateTimeKind.Utc),
        reader.IsDBNull(offset + 6) ? null : Enum.Parse<ExecutionReview>(reader.GetString(offset + 6)),
        DateTime.SpecifyKind(reader.GetDateTime(offset + 7), DateTimeKind.Utc));

    private static string? ValidateTrade(TradeEvidenceWrite input, out string symbol, out string currency, out string? note)
    {
        symbol = input.Symbol?.Trim().ToUpperInvariant() ?? "";
        currency = input.Currency?.Trim().ToUpperInvariant() ?? "";
        note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        if (symbol.Length is < 1 or > 32) return "invalid_symbol";
        if (input.Quantity <= 0 || input.Price <= 0) return "quantity_and_price_must_be_positive";
        if (currency.Length != 3 || !currency.All(character => character is >= 'A' and <= 'Z')) return "invalid_currency";
        if (note?.Length > MaxTextLength) return "note_too_long";
        return null;
    }

    private static void AddTradeParameters(NpgsqlCommand command, Guid id, Guid userId, TradeEvidenceWrite input, string symbol, string currency, string? note, Guid decisionId)
    {
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(userId);
        AddTradeValues(command, input, symbol, currency, note);
        command.Parameters.AddWithValue(decisionId);
    }

    private static void AddTradeValues(NpgsqlCommand command, TradeEvidenceWrite input, string symbol, string currency, string? note)
    {
        command.Parameters.AddWithValue(symbol);
        command.Parameters.AddWithValue(input.Side.ToString());
        command.Parameters.AddWithValue(input.Quantity);
        command.Parameters.AddWithValue(input.Price);
        command.Parameters.AddWithValue(currency);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = input.ExecutedAt.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)note ?? DBNull.Value });
    }

    internal static TradeEvidenceResponse ReadTrade(NpgsqlDataReader reader, int offset = 0) => new(
        reader.GetGuid(offset), reader.GetGuid(offset + 1), reader.GetString(offset + 2), Enum.Parse<TradeSide>(reader.GetString(offset + 3)),
        reader.GetDecimal(offset + 4), reader.GetDecimal(offset + 5), reader.GetString(offset + 6).Trim(),
        DateTime.SpecifyKind(reader.GetDateTime(offset + 7), DateTimeKind.Utc), reader.IsDBNull(offset + 8) ? null : reader.GetString(offset + 8),
        DateTime.SpecifyKind(reader.GetDateTime(offset + 9), DateTimeKind.Utc), DateTime.SpecifyKind(reader.GetDateTime(offset + 10), DateTimeKind.Utc));
}
