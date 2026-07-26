using System.Text.Json;

public record DiaryWrite(DateOnly LocalDate, string Title, string? Content, IReadOnlyList<string>? Tags = null);
record QuickNote(DateOnly LocalDate, string Content, Guid? TargetDiaryId);
public record DiaryResponse(Guid Id, DateOnly LocalDate, string Title, string Content, DateTime CreatedAt, DateTime UpdatedAt, IReadOnlyList<string> Tags);
record TransactionWrite(string Symbol, string Side, decimal Quantity, decimal Price, string Currency, DateTime TradedAt, string? Notes);
record TransactionResponse(Guid Id, Guid DiaryId, string Symbol, string Side, decimal Quantity, decimal Price, string Currency, DateTime TradedAt, string Notes, DateTime CreatedAt, DateTime UpdatedAt);
record StoredResult(int StatusCode, string? Location, JsonElement Body);
record QuickNoteResponse(Guid? DiaryId, bool Appended);
record QuickObservationWrite(string Content);
record QuickObservationResponse(Guid MarketObservationId, Guid ObservationUpdateId, DateOnly JournalDay, DateTime RecordedAt, bool Appended);
enum ObservationSubjectType { broad_market, sector, theme, instrument }
record ObservationSubjectWrite(ObservationSubjectType Type, string? Name = null, Guid? InstrumentId = null, string? Market = null, string? Symbol = null, string? DisplayName = null);
record ObservationSubjectResponse(ObservationSubjectType Type, string? Name, Guid? InstrumentId, string? Market, string? Symbol, string? DisplayName, bool DailyCloseAvailable);
record ObservationEvidenceWrite(string Url, string? Title = null, string? Quote = null);
record ObservationEvidenceResponse(string Url, string? Title, string? Quote);
record ObservationUpdateWrite(
    string Content,
    string? Signal = null,
    string? Interpretation = null,
    string? MentalState = null,
    IReadOnlyList<string>? Tags = null,
    ObservationSubjectWrite? PrimarySubject = null,
    IReadOnlyList<ObservationSubjectWrite>? RelatedSubjects = null,
    ObservationEvidenceWrite? Evidence = null);
record ObservationUpdateResponse(
    Guid Id,
    string Content,
    DateTime RecordedAt,
    DateTime UpdatedAt,
    string? Signal,
    string? Interpretation,
    string? MentalState,
    IReadOnlyList<string> Tags,
    ObservationSubjectResponse? PrimarySubject,
    IReadOnlyList<ObservationSubjectResponse> RelatedSubjects,
    ObservationEvidenceResponse? Evidence);
record ObservationUpdateEditResponse(
    Guid Id,
    string Content,
    DateTime RecordedAt,
    DateTime UpdatedAt,
    bool HonestyReminderRequired,
    string? Signal,
    string? Interpretation,
    string? MentalState,
    IReadOnlyList<string> Tags,
    ObservationSubjectResponse? PrimarySubject,
    IReadOnlyList<ObservationSubjectResponse> RelatedSubjects,
    ObservationEvidenceResponse? Evidence);
record MarketObservationResponse(Guid Id, DateOnly JournalDay, string Timezone, string Rollover, IReadOnlyList<ObservationUpdateResponse> Updates);
record DiaryDaySummaryItem(DateOnly LocalDate, long DiaryCount, long TransactionCount);
record CollectionResponse<T>(List<T> Items);
/// <summary>Sanitized diary projection for partner compare. No transactions/reviews/internal IDs.</summary>
public record PartnerDiaryItem(Guid Id, DateOnly LocalDate, string Title, string Content, IReadOnlyList<string> Tags);

// ponytail: WithOpenApi parameter mutations are dropped by .NET 10 doc generation (hence its deprecation),
// so the Idempotency-Key header is surfaced via a marker + operation transformer instead.
sealed record IdempotencyKeyHeaderMarker;
