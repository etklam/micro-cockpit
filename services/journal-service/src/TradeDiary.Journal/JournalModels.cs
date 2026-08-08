using System.Text.Json;

record StoredResult(int StatusCode, string? Location, JsonElement Body);
record QuickObservationWrite(string Content, string? SourceLabel = null, DateOnly? JournalDay = null);
record QuickObservationResponse(Guid MarketObservationId, Guid ObservationUpdateId, DateOnly JournalDay, DateTime RecordedAt, bool Appended);
enum ObservationSubjectType { broad_market, sector, theme, instrument }
record ObservationSubjectWrite(ObservationSubjectType Type, string? Name = null, Guid? InstrumentId = null, string? Market = null, string? Symbol = null, string? DisplayName = null);
enum DailyCloseStatus { available, unavailable, unsupported }
record DailyCloseEvidenceResponse(DateOnly TradingDate, decimal RawClose, decimal AdjustedClose, string Provider, DateTime PublishedAt);
record ObservationSubjectResponse(
    ObservationSubjectType Type,
    string? Name,
    Guid? InstrumentId,
    string? Market,
    string? Symbol,
    string? DisplayName,
    bool DailyCloseAvailable,
    DailyCloseStatus DailyCloseStatus = DailyCloseStatus.unsupported,
    DailyCloseEvidenceResponse? DailyClose = null);
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
    ObservationEvidenceWrite? Evidence = null,
    string? SourceLabel = null);
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
record ObservationSearchItemResponse(Guid MarketObservationId, DateOnly JournalDay, Guid AuthorId, ObservationUpdateResponse Update);
record ObservationSearchPage(IReadOnlyList<ObservationSearchItemResponse> Items, string? NextCursor);
record MarketObservationDaySummaryItem(DateOnly Date, Guid MarketObservationId, long UpdateCount, long? ReadyForReviewCount);
enum ExpectationConfidence { low, medium, high }
enum ExpectationReadiness { active, ready_for_review, reviewed }
enum ExpectationDeadlinePreset { next_trading_day, five_trading_days }
enum ExpectationOutcome { confirmed, partially_confirmed, invalidated, indeterminate }
enum ReasoningQuality { sound, mixed, weak }
enum ReasoningLabelKind { issue, strength }
record ExpectationWrite(
    string ExpectedBehavior,
    DateTimeOffset? Deadline,
    string InvalidationCondition,
    ExpectationConfidence Confidence,
    string Market,
    ExpectationDeadlinePreset? DeadlinePreset = null);
record ExpectationResponse(
    Guid Id,
    Guid ObservationUpdateId,
    Guid MarketObservationId,
    DateOnly JournalDay,
    string ExpectedBehavior,
    DateTime Deadline,
    string InvalidationCondition,
    ExpectationConfidence Confidence,
    string Market,
    DateTime? InvalidatedAt,
    ExpectationReadiness Readiness,
    bool DeadlineElapsed,
    DateTime CreatedAt,
    DateTime UpdatedAt);
record ExpectationEditResponse(
    Guid Id,
    Guid ObservationUpdateId,
    Guid MarketObservationId,
    DateOnly JournalDay,
    string ExpectedBehavior,
    DateTime Deadline,
    string InvalidationCondition,
    ExpectationConfidence Confidence,
    string Market,
    DateTime? InvalidatedAt,
    ExpectationReadiness Readiness,
    bool DeadlineElapsed,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool HonestyReminderRequired);
record ExpectationReviewWrite(
    ExpectationOutcome Outcome,
    ReasoningQuality ReasoningQuality,
    string? Explanation,
    IReadOnlyList<string>? SystemIssueKeys = null,
    IReadOnlyList<string>? SystemStrengthKeys = null,
    IReadOnlyList<Guid>? CustomLabelIds = null);
record ReasoningLabelResponse(Guid? Id, ReasoningLabelKind Kind, string Key, string Name, bool IsSystem);
record ExpectationReviewResponse(
    Guid Id,
    Guid ExpectationId,
    ExpectationOutcome Outcome,
    ReasoningQuality ReasoningQuality,
    string? Explanation,
    IReadOnlyList<ReasoningLabelResponse> Labels,
    DateTime CreatedAt,
    DateTime UpdatedAt);
enum ReviewContextAvailability { available, partial }
record ReviewContextObservationResponse(Guid Id, DateOnly JournalDay);
record ReviewContextActionDecisionResponse(
    ActionDecisionResponse Decision,
    IReadOnlyList<TradeEvidenceResponse> Trades);
record ExpectationReviewContextResponse(
    Guid ExpectationId,
    Guid ObservationUpdateId,
    ReviewContextAvailability Availability,
    IReadOnlyList<string> UnavailableContext,
    ReviewContextObservationResponse? MarketObservation,
    ObservationUpdateResponse? ObservationUpdate,
    IReadOnlyList<ReviewContextActionDecisionResponse> ActionDecisions);
record ReasoningLabelWrite(ReasoningLabelKind Kind, string Name);
enum ActionDecisionIntent { trade, continue_observing, avoid_trade }
enum ExecutionReview { followed, partially_followed, deviated }
enum TradeSide { buy, sell }
record ActionDecisionWrite(ActionDecisionIntent Intent, string Reason, Guid? ExpectationId = null, ExecutionReview? ExecutionReview = null);
record ActionDecisionResponse(
    Guid Id,
    Guid ObservationUpdateId,
    Guid? ExpectationId,
    ActionDecisionIntent Intent,
    string Reason,
    DateTime RecordedAt,
    ExecutionReview? ExecutionReview,
    DateTime UpdatedAt);
record ActionDecisionEditResponse(
    Guid Id,
    Guid ObservationUpdateId,
    Guid? ExpectationId,
    ActionDecisionIntent Intent,
    string Reason,
    DateTime RecordedAt,
    ExecutionReview? ExecutionReview,
    DateTime UpdatedAt,
    bool HonestyReminderRequired);
record TradeEvidenceWrite(string Symbol, TradeSide Side, decimal Quantity, decimal Price, string Currency, DateTimeOffset ExecutedAt, string? Note = null);
record TradeEvidenceResponse(
    Guid Id,
    Guid ActionDecisionId,
    string Symbol,
    TradeSide Side,
    decimal Quantity,
    decimal Price,
    string Currency,
    DateTime ExecutedAt,
    string? Note,
    DateTime CreatedAt,
    DateTime UpdatedAt);
record WatchlistItemResponse(Guid InstrumentId, string? Note, DateTime CreatedAt, DateTime UpdatedAt);
record WatchlistCreateWrite(string Note);
record WatchlistNoteWrite(string? Note);
record PatternEvidenceResponse(
    Guid ExpectationId,
    Guid ReviewId,
    DateOnly JournalDay,
    string Subject,
    string ExpectedBehavior,
    ExpectationOutcome Outcome,
    ReasoningQuality ReasoningQuality,
    string ObservationExcerpt,
    string? ReviewExplanation,
    DateTime ReviewedAt,
    string Url);
enum PatternTrendStatus { supported, insufficient_evidence }
enum PatternTrendDirection { higher, lower, same }
record PatternTrendBucketResponse(
    DateOnly From,
    DateOnly To,
    int OccurrenceCount,
    int ReviewedExpectationCount,
    IReadOnlyList<PatternEvidenceResponse> Evidence);
record PatternTrendResponse(
    PatternTrendStatus Status,
    PatternTrendDirection? Direction,
    PatternTrendBucketResponse Current,
    PatternTrendBucketResponse? Previous);
record PatternLabelResponse(
    ReasoningLabelKind Kind,
    string Key,
    string Name,
    bool System,
    int Count,
    int Denominator,
    Guid? ConfirmedPatternId,
    bool PatternIsConfirmed,
    DateTime? FirstSeen,
    DateTime? MostRecent,
    IReadOnlyList<PatternEvidenceResponse> Evidence,
    PatternTrendResponse Trend);
record PatternReviewResponse(
    DateOnly From,
    DateOnly To,
    int ReviewedExpectationCount,
    IReadOnlyList<PatternLabelResponse> Labels);
record ConfirmedPatternCreate(ReasoningLabelKind Kind, string Key);
record ConfirmedPatternResponse(
    Guid Id,
    ReasoningLabelKind Kind,
    string Key,
    string Name,
    bool System,
    bool IsConfirmed,
    DateTime FirstConfirmedAt,
    DateTime ConfirmedAt,
    DateTime? UnconfirmedAt,
    DateTime UpdatedAt);
enum DisciplinePrincipleStatus { active, disabled, archived }
record DisciplinePrincipleCreate(string Content, Guid? ConfirmedPatternId = null);
record DisciplinePrincipleUpdate(string Content, DisciplinePrincipleStatus Status);
record DisciplinePrincipleResponse(
    Guid Id,
    string Content,
    DisciplinePrincipleStatus Status,
    bool SelectedForToday,
    Guid? ConfirmedPatternId,
    string? ConfirmedPatternLabel,
    DateTime CreatedAt,
    DateTime UpdatedAt);
record TrackedInstrumentResponse(Guid InstrumentId);
record TrackedInstrumentSetResponse(int ContractVersion, DateTime GeneratedAt, IReadOnlyList<TrackedInstrumentResponse> Items);
record CollectionResponse<T>(List<T> Items);

// ponytail: WithOpenApi parameter mutations are dropped by .NET 10 doc generation (hence its deprecation),
// so the Idempotency-Key header is surfaced via a marker + operation transformer instead.
sealed record IdempotencyKeyHeaderMarker;
