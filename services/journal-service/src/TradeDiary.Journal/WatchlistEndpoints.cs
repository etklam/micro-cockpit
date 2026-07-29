using System.Net;
using Npgsql;
using NpgsqlTypes;

static class WatchlistEndpoints
{
    internal static void Map(RouteGroupBuilder journal)
    {
        journal.MapGet("/watchlist", async (HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var command = db.CreateCommand("""
                SELECT instrument_id,note,created_at,updated_at
                FROM journal.watchlist_items WHERE user_id=$1 ORDER BY created_at,instrument_id
                """);
            command.Parameters.AddWithValue(userId);
            var items = new List<WatchlistItemResponse>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) items.Add(Read(reader));
            return Results.Ok(new CollectionResponse<WatchlistItemResponse>(items));
        }).Produces<CollectionResponse<WatchlistItemResponse>>(200).ProducesProblem(401);

        journal.MapPost("/watchlist/{instrumentId:guid}", async (Guid instrumentId, HttpRequest request, NpgsqlDataSource db, IHttpClientFactory clients, CancellationToken cancellationToken) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            try
            {
                using var response = await clients.CreateClient("market-data").GetAsync($"/internal/v1/instruments/{instrumentId}", cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound) return Results.Problem("not_found", statusCode: 404);
                if (!response.IsSuccessStatusCode) return Results.Problem("instrument_directory_unavailable", statusCode: 503);
            }
            catch (HttpRequestException)
            {
                return Results.Problem("instrument_directory_unavailable", statusCode: 503);
            }
            await using var command = db.CreateCommand("""
                INSERT INTO journal.watchlist_items(user_id,instrument_id) VALUES($1,$2)
                ON CONFLICT DO NOTHING
                RETURNING instrument_id,note,created_at,updated_at
                """);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(instrumentId);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? Results.Created($"/internal/watchlist/{instrumentId}", Read(reader))
                : Results.NoContent();
        }).Produces<WatchlistItemResponse>(201).Produces(204).ProducesProblem(401).ProducesProblem(404).ProducesProblem(503);

        journal.MapPut("/watchlist/{instrumentId:guid}/note", async (Guid instrumentId, WatchlistNoteWrite input, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
            if (note?.Length > 500) return Results.Problem("note_too_long", statusCode: 400);
            await using var command = db.CreateCommand("""
                UPDATE journal.watchlist_items SET note=$3,updated_at=now()
                WHERE user_id=$1 AND instrument_id=$2
                RETURNING instrument_id,note,created_at,updated_at
                """);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(instrumentId);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)note ?? DBNull.Value });
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Results.Ok(Read(reader)) : Results.Problem("not_found", statusCode: 404);
        }).Produces<WatchlistItemResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

        journal.MapDelete("/watchlist/{instrumentId:guid}", async (Guid instrumentId, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var command = db.CreateCommand("DELETE FROM journal.watchlist_items WHERE user_id=$1 AND instrument_id=$2");
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(instrumentId);
            return await command.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
        }).Produces(204).ProducesProblem(401).ProducesProblem(404);
    }

    private static WatchlistItemResponse Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1),
        DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc),
        DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc));
}
