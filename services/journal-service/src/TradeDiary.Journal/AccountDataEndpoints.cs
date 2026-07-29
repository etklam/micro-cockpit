using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

static class AccountDataEndpoints
{
    internal static void Map(RouteGroupBuilder journal)
    {
        journal.MapGet("/account-export", async (
            HttpRequest request, NpgsqlDataSource db, IHttpClientFactory clients) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var ownerIds = await OwnerIds(request, userId, clients);
            if (ownerIds is null) return Results.Problem("identity_unavailable", statusCode: 503);
            await using var connection = await db.OpenConnectionAsync();
            return Results.Ok(new AccountJournalExport(
                await Rows(connection, "SELECT * FROM journal.market_observations WHERE user_id=ANY($1) ORDER BY journal_day,id", ownerIds),
                await Rows(connection, "SELECT * FROM journal.observation_updates WHERE user_id=ANY($1) ORDER BY recorded_at,id", ownerIds),
                await Rows(connection, "SELECT * FROM journal.expectations WHERE user_id=ANY($1) ORDER BY created_at,id", ownerIds),
                await Rows(connection, "SELECT * FROM journal.expectation_reviews WHERE user_id=ANY($1) ORDER BY created_at,id", ownerIds),
                await Rows(connection, """
                    SELECT l.* FROM journal.expectation_review_labels l
                    JOIN journal.expectation_reviews r ON r.id=l.review_id
                    WHERE r.user_id=ANY($1) ORDER BY l.review_id,l.id
                    """, ownerIds),
                await Rows(connection, "SELECT * FROM journal.reasoning_labels WHERE user_id=ANY($1) ORDER BY created_at,id", ownerIds),
                await Rows(connection, "SELECT * FROM journal.action_decisions WHERE user_id=ANY($1) ORDER BY recorded_at,id", ownerIds),
                await Rows(connection, "SELECT * FROM journal.trades WHERE user_id=ANY($1) ORDER BY executed_at,id", ownerIds),
                await Rows(connection, "SELECT * FROM journal.watchlist_items WHERE user_id=ANY($1) ORDER BY created_at,instrument_id", ownerIds),
                await Rows(connection, "SELECT * FROM journal.discipline_principles WHERE user_id=ANY($1) ORDER BY created_at,id", ownerIds),
                await Rows(connection, "SELECT * FROM journal.agent_access_grants WHERE owner_user_id=ANY($1) ORDER BY created_at,id", ownerIds),
                await Rows(connection, """
                    SELECT r.* FROM journal.agent_access_grant_records r
                    JOIN journal.agent_access_grants g ON g.id=r.grant_id
                    WHERE g.owner_user_id=ANY($1) ORDER BY r.grant_id,r.record_type,r.record_id
                    """, ownerIds)));
        })
        .Produces<AccountJournalExport>(200).ProducesProblem(401).ProducesProblem(503);

        journal.MapDelete("/account-data", async (
            HttpRequest request, NpgsqlDataSource db, IHttpClientFactory clients) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var ownerIds = await OwnerIds(request, userId, clients);
            if (ownerIds is null) return Results.Problem("identity_unavailable", statusCode: 503);
            await using var connection = await db.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            foreach (var sql in new[]
            {
                "DELETE FROM journal.agent_access_grants WHERE owner_user_id=ANY($1) OR agent_user_id=ANY($1)",
                "DELETE FROM journal.expectation_review_labels l USING journal.expectation_reviews r WHERE l.review_id=r.id AND r.user_id=ANY($1)",
                "DELETE FROM journal.expectation_reviews WHERE user_id=ANY($1)",
                "DELETE FROM journal.trades WHERE user_id=ANY($1)",
                "DELETE FROM journal.action_decisions WHERE user_id=ANY($1)",
                "DELETE FROM journal.expectations WHERE user_id=ANY($1)",
                "DELETE FROM journal.observation_updates WHERE user_id=ANY($1)",
                "DELETE FROM journal.market_observations WHERE user_id=ANY($1)",
                "DELETE FROM journal.reasoning_labels WHERE user_id=ANY($1)",
                "DELETE FROM journal.watchlist_items WHERE user_id=ANY($1)",
                "DELETE FROM journal.discipline_principles WHERE user_id=ANY($1)",
                "DELETE FROM journal.idempotency_keys WHERE user_id=ANY($1)",
            })
            {
                await using var command = new NpgsqlCommand(sql, connection, tx);
                command.Parameters.AddWithValue(ownerIds);
                await command.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return Results.NoContent();
        })
        .Produces(204).ProducesProblem(401).ProducesProblem(503);
    }

    private static async Task<Guid[]?> OwnerIds(HttpRequest request, Guid userId, IHttpClientFactory clients)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "/internal/auth/agents");
        if (AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var authorization))
            message.Headers.Authorization = authorization;
        try
        {
            using var response = await clients.CreateClient("identity").SendAsync(message, request.HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode) return null;
            var agents = await response.Content.ReadFromJsonAsync<AgentCollection>(cancellationToken: request.HttpContext.RequestAborted);
            return agents is null ? null : [userId, .. agents.Items.Select(item => item.UserId)];
        }
        catch (HttpRequestException) { return null; }
    }

    private static async Task<JsonElement> Rows(NpgsqlConnection connection, string sql, Guid[] ownerIds)
    {
        await using var command = new NpgsqlCommand($"SELECT COALESCE(jsonb_agg(to_jsonb(row_data)), '[]'::jsonb) FROM ({sql}) row_data", connection);
        command.Parameters.AddWithValue(ownerIds);
        return JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!).RootElement.Clone();
    }

    private sealed record AgentCollection(IReadOnlyList<AgentItem> Items);
    private sealed record AgentItem(Guid UserId);
}

record AccountJournalExport(
    JsonElement MarketObservations,
    JsonElement ObservationUpdates,
    JsonElement Expectations,
    JsonElement ExpectationReviews,
    JsonElement ExpectationReviewLabels,
    JsonElement ReasoningLabels,
    JsonElement ActionDecisions,
    JsonElement Trades,
    JsonElement WatchlistItems,
    JsonElement DisciplinePrinciples,
    JsonElement AccessGrants,
    JsonElement AccessGrantRecords);
