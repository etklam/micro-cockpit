using System.Security.Cryptography;
using System.Text;
using Npgsql;

static class TrackedInstrumentEndpoints
{
    internal static bool HasServiceKey(HttpContext context, IConfiguration configuration)
    {
        var supplied = Encoding.UTF8.GetBytes(context.Request.Headers["X-Service-Key"].ToString());
        var expected = Encoding.UTF8.GetBytes(configuration["Internal:ServiceKey"] ?? "");
        return expected.Length > 0 && supplied.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    internal static void Map(WebApplication app)
    {
        app.MapGet("/internal/v1/tracked-us-instruments", async (NpgsqlDataSource db, TimeProvider timeProvider) =>
        {
            await using var command = db.CreateCommand("""
                SELECT DISTINCT instrument_id
                FROM (
                    SELECT instrument_id
                    FROM journal.watchlist_items

                    UNION ALL

                    SELECT (subject->>'instrumentId')::uuid
                    FROM journal.expectations e
                    JOIN journal.observation_updates u
                      ON u.id=e.observation_update_id AND u.user_id=e.user_id
                    CROSS JOIN LATERAL (
                        SELECT u.primary_subject AS subject
                        UNION ALL
                        SELECT value FROM jsonb_array_elements(u.related_subjects)
                    ) subjects
                    WHERE e.deleted_at IS NULL AND u.deleted_at IS NULL
                      AND e.invalidated_at IS NULL AND e.deadline > $1
                      AND subject->>'market'='US' AND subject->>'instrumentId' IS NOT NULL

                    UNION ALL

                    SELECT (subject->>'instrumentId')::uuid
                    FROM journal.observation_updates u
                    CROSS JOIN LATERAL (
                        SELECT u.primary_subject AS subject
                        UNION ALL
                        SELECT value FROM jsonb_array_elements(u.related_subjects)
                    ) subjects
                    WHERE u.deleted_at IS NULL AND u.recorded_at >= $1 - interval '30 days'
                      AND subject->>'market'='US' AND subject->>'instrumentId' IS NOT NULL
                ) tracked(instrument_id)
                ORDER BY instrument_id
                """);
            command.Parameters.AddWithValue(timeProvider.GetUtcNow().UtcDateTime);
            await using var reader = await command.ExecuteReaderAsync();
            var items = new List<TrackedInstrumentResponse>();
            while (await reader.ReadAsync()) items.Add(new(reader.GetGuid(0)));
            return Results.Ok(new TrackedInstrumentSetResponse(
                1, timeProvider.GetUtcNow().UtcDateTime, items));
        })
        .RequireAuthorization("serviceKey")
        .Produces<TrackedInstrumentSetResponse>(200)
        .ProducesProblem(403);
    }
}
