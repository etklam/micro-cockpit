using System.Text.Json;
using Npgsql;
using TradeDiary.Events;

sealed class OutboxPublisher(
    NpgsqlDataSource db,
    IHttpClientFactory clients,
    IConfiguration configuration,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PublishOnce(stoppingToken); }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(error, "Journal outbox publish failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    async Task PublishOnce(CancellationToken cancellationToken)
    {
        await using var command = db.CreateCommand(
            "SELECT event_id,event_type,event_version,payload::text FROM journal.outbox_events WHERE published_at IS NULL ORDER BY occurred_at LIMIT 20");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<(Guid Id, string Type, int Version, string Payload)>();
        while (await reader.ReadAsync(cancellationToken))
            events.Add((reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));

        foreach (var item in events)
        {
            if (item.Type != DiaryDeletedV1Envelope.Type || item.Version != DiaryDeletedV1Envelope.EventVersion) continue;
            var payload = JsonSerializer.Deserialize<DiaryDeletedV1>(item.Payload, JsonSerializerOptions.Web)
                ?? throw new JsonException("DiaryDeleted.v1 payload is required.");
            var deleted = DiaryDeletedV1Envelope.Create(item.Id, payload.DiaryId, payload.UserId);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/events/diary-deleted");
            request.Headers.Add("X-Service-Key", configuration["Internal:ServiceKey"] ?? throw new InvalidOperationException("Internal:ServiceKey is required."));
            request.Content = JsonContent.Create(deleted, options: JsonSerializerOptions.Web);
            using var response = await clients.CreateClient("reminder").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) continue;
            await using var mark = db.CreateCommand("UPDATE journal.outbox_events SET published_at=now() WHERE event_id=$1 AND published_at IS NULL");
            mark.Parameters.AddWithValue(item.Id);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
