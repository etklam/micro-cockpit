namespace TradeDiary.Events;

public sealed record RecordDeletedV1Envelope(
    Guid EventId,
    Guid RecordId,
    string RecordType,
    int Version,
    string Operation,
    DateTime EventTime)
{
    public const string Type = "RecordDeleted.v1";
    public const int EventVersion = 1;
    public static readonly IReadOnlySet<string> RecordTypes = new HashSet<string>
    {
        "market_observation", "observation_update", "expectation",
        "expectation_review", "action_decision", "trade",
    };

    public static bool IsValid(RecordDeletedV1Envelope? input) =>
        input is not null
        && input.EventId != Guid.Empty
        && input.RecordId != Guid.Empty
        && RecordTypes.Contains(input.RecordType)
        && input.Version == EventVersion
        && input.Operation == "deleted"
        && input.EventTime != default;
}
