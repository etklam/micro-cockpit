namespace TradeDiary.Events;

/// <summary>Wire contract for DiaryDeleted.v1 — one module for producer and consumer.</summary>
public sealed record DiaryDeletedV1(Guid DiaryId, Guid UserId);

public sealed record DiaryDeletedV1Envelope(
    Guid EventId,
    string EventType,
    int Version,
    DiaryDeletedV1? Payload)
{
    public const string Type = "DiaryDeleted.v1";
    public const int EventVersion = 1;

    public static DiaryDeletedV1Envelope Create(Guid eventId, Guid diaryId, Guid userId)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Event ID is required.", nameof(eventId));
        if (diaryId == Guid.Empty) throw new ArgumentException("Diary ID is required.", nameof(diaryId));
        if (userId == Guid.Empty) throw new ArgumentException("User ID is required.", nameof(userId));
        return new(eventId, Type, EventVersion, new DiaryDeletedV1(diaryId, userId));
    }

    public static bool IsValid(DiaryDeletedV1Envelope? input) =>
        input is not null &&
        input.EventId != Guid.Empty &&
        input.EventType == Type &&
        input.Version == EventVersion &&
        input.Payload is { } payload &&
        payload.DiaryId != Guid.Empty &&
        payload.UserId != Guid.Empty;
}
