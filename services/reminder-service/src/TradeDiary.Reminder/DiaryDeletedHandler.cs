using System.Text.Json;
using Npgsql;

public sealed record DiaryDeletedV1(Guid DiaryId, Guid UserId);

public sealed record DiaryDeletedV1Envelope(
    Guid EventId,
    string EventType,
    int Version,
    DiaryDeletedV1? Payload);

public static class DiaryDeletedHandler
{
    public const string EventType = "DiaryDeleted.v1";
    public const int EventVersion = 1;

    public static bool IsValid(DiaryDeletedV1Envelope? input) =>
        input is not null &&
        input.EventId != Guid.Empty &&
        input.EventType == EventType &&
        input.Version == EventVersion &&
        input.Payload is { } payload &&
        payload.DiaryId != Guid.Empty &&
        payload.UserId != Guid.Empty;

    public static async Task ProcessAsync(
        NpgsqlDataSource db,
        DiaryDeletedV1Envelope input,
        CancellationToken cancellationToken = default)
    {
        if (!IsValid(input)) throw new ArgumentException("Invalid DiaryDeleted.v1 event.", nameof(input));

        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        await using var inbox = new NpgsqlCommand(
            "INSERT INTO reminder.inbox_events(event_id,event_type,event_version,payload) VALUES($1,$2,$3,$4::jsonb) ON CONFLICT DO NOTHING",
            connection,
            tx);
        inbox.Parameters.AddWithValue(input.EventId);
        inbox.Parameters.AddWithValue(input.EventType);
        inbox.Parameters.AddWithValue(input.Version);
        inbox.Parameters.AddWithValue(JsonSerializer.Serialize(input.Payload, JsonSerializerOptions.Web));

        if (await inbox.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            await tx.CommitAsync(cancellationToken);
            return;
        }

        await using var expire = new NpgsqlCommand(
            "UPDATE reminder.diary_alerts SET status='expired',next_local_date=NULL,next_trigger_at=NULL,updated_at=now() WHERE diary_id=$1 AND user_id=$2 AND status='active'",
            connection,
            tx);
        expire.Parameters.AddWithValue(input.Payload!.DiaryId);
        expire.Parameters.AddWithValue(input.Payload.UserId);
        await expire.ExecuteNonQueryAsync(cancellationToken);

        await using var processed = new NpgsqlCommand(
            "UPDATE reminder.inbox_events SET processed_at=now() WHERE event_id=$1",
            connection,
            tx);
        processed.Parameters.AddWithValue(input.EventId);
        await processed.ExecuteNonQueryAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }
}
