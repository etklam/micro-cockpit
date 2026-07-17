using System.Text.Json.Serialization;

public enum CapabilityStatus
{
    Available,
    Empty,
    Unavailable
}

public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string DisplayName);

public sealed record AppBootstrapResponse(
    CurrentUserResponse CurrentUser,
    string Timezone,
    string BaseCurrency,
    string Role,
    string AccountType,
    DateOnly CurrentLocalDate,
    IReadOnlyList<string> AvailableProductAreas);

public sealed record DashboardResponse(
    DateOnly LocalDate,
    DashboardDiaryResponse Diary,
    DailyPerformanceResponse? Performance,
    long? PendingAlerts,
    DisciplineResponse? Discipline,
    IReadOnlyList<DiaryResponse> RecentDiaries,
    DashboardCapabilitiesResponse Capabilities);

public sealed record DashboardDiaryResponse(bool WrittenToday, long Count);
public sealed record DashboardCapabilitiesResponse(CapabilityStatus Alerts, CapabilityStatus Discipline);

public sealed record CalendarResponse(
    int Year,
    int Month,
    MonthSummaryResponse? Summary,
    IReadOnlyList<CalendarDayResponse> Days,
    CalendarCapabilitiesResponse Capabilities);

public sealed record CalendarDayResponse(
    DateOnly Date,
    DailyPerformanceResponse? Performance,
    long DiaryCount,
    long TransactionCount,
    long? AlertCount);

public sealed record CalendarCapabilitiesResponse(CapabilityStatus Alerts);

public sealed record StockPageResponse(
    StockResponse Stock,
    BarsResponse? Bars,
    StockPageCapabilitiesResponse Capabilities);

public sealed record StockPageCapabilitiesResponse(CapabilityStatus MarketData);

public sealed record DiaryResponse(
    Guid Id,
    DateOnly LocalDate,
    string Title,
    string Content,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<string> Tags);

public sealed record DiaryPageResponse(
    IReadOnlyList<DiaryResponse> Items,
    string? NextCursor);

public sealed record DiaryReviewWrite(
    string? Thesis,
    string? PlannedAction,
    string? ActualAction,
    string? Emotion,
    short? DisciplineScore,
    short? ExecutionScore,
    string? ProcessAssessment,
    IReadOnlyList<string>? MistakeTags,
    string? Lesson,
    string? NextAction);

public sealed record DiaryReviewResponse(
    Guid DiaryId,
    string? Thesis,
    string? PlannedAction,
    string? ActualAction,
    string? Emotion,
    short? DisciplineScore,
    short? ExecutionScore,
    string? ProcessAssessment,
    IReadOnlyList<string> MistakeTags,
    string? Lesson,
    string? NextAction,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record DiaryReviewSummaryResponse(
    long ReviewedCount,
    decimal? AverageDisciplineScore,
    decimal? AverageExecutionScore,
    IReadOnlyDictionary<string, long> EmotionCounts,
    IReadOnlyDictionary<string, long> ProcessAssessmentCounts,
    IReadOnlyList<MistakeTagCountResponse> TopMistakeTags);

public sealed record MistakeTagCountResponse(string Tag, long Count);

public enum DiaryReviewStatus { reviewed, unreviewed }
public enum DiaryReviewFilterStatus { all, reviewed, unreviewed }
public enum DiaryReviewProcessAssessment { good, mixed, poor }
public enum DiaryReviewAssessmentFilter { all, good, mixed, poor }

public sealed record DiaryReviewItemsResponse(IReadOnlyList<DiaryReviewItemResponse> Items, string? NextCursor);
public sealed record DiaryReviewItemResponse(
    Guid DiaryId,
    DateOnly LocalDate,
    string Title,
    string ContentPreview,
    DiaryReviewStatus ReviewStatus,
    DiaryReviewProcessAssessment? ProcessAssessment,
    string? Emotion,
    short? DisciplineScore,
    short? ExecutionScore,
    IReadOnlyList<string> MistakeTags,
    string? Lesson,
    string? NextAction,
    DateTime? ReviewUpdatedAt);

public sealed record RotationMonitorResponse(
    RotationUniverseResponse Universe,
    DateOnly? SnapshotDate,
    string FormulaVersion,
    string Status,
    RotationMarketStateResponse MarketState,
    IReadOnlyList<RotationSectorBreadthResponse> SectorBreadth,
    IReadOnlyList<RotationEtfSnapshotResponse> Etfs);

public sealed record RotationUniverseResponse(Guid Id, string Code, string Name, string RankScope);
public sealed record RotationMarketStateResponse(string? State, decimal? BreadthPercent, bool? BenchmarkAboveMa200, string Status);
public sealed record RotationSectorBreadthResponse(
    string Sector,
    int MemberCount,
    int AvailableCount,
    decimal? AboveMa20Percent,
    decimal? AboveMa50Percent,
    decimal? AboveMa200Percent,
    string Status);
public sealed record RotationEtfSnapshotResponse(
    string Symbol,
    string Label,
    string? Sector,
    decimal? Close,
    decimal? Return2w,
    decimal? Return1m,
    decimal? Return3m,
    int? Rank2w,
    string? RankGroup,
    decimal? Percentile2w,
    bool? AboveMa20,
    bool? AboveMa50,
    bool? AboveMa200,
    string Status);

public sealed record DailyPerformanceResponse(
    DateOnly LocalDate,
    decimal PnlAmount,
    decimal? CapitalBase,
    decimal? PnlPercent,
    string Note);

public sealed record DisciplineResponse(
    Guid Id,
    string Content,
    int Position,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record MonthSummaryResponse(
    int Year,
    int Month,
    decimal Total,
    long RecordedDays,
    long ProfitDays,
    long LossDays,
    long FlatDays,
    decimal? BestDay,
    decimal? WorstDay);

public sealed record StockResponse(
    Guid Id,
    string Symbol,
    string Name,
    string Exchange,
    string AssetType,
    DateTime CreatedAt);

public sealed record BarsResponse(int ContractVersion, string Symbol, IReadOnlyList<BarResponse> Items);
public sealed record BarResponse(
    DateOnly TradingDate,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    string Provider,
    DateTime PublishedAt);

internal sealed record IdentityUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string Timezone,
    string BaseCurrency,
    string Role,
    string AccountType,
    string Status,
    int StatusVersion);

internal sealed record IdentityTokensResponse(string AccessToken, DateTime ExpiresAt, string RefreshToken);
internal sealed record RefreshRequest(string RefreshToken);

internal sealed record CollectionResponse<T>(IReadOnlyList<T> Items);
internal sealed record DiaryDayFact(DateOnly LocalDate, long DiaryCount, long TransactionCount);
internal sealed record DayAlertFact(DateOnly Date, long Count);
internal sealed record CalendarAlertFact(DateOnly LocalDate, long Count);

public sealed record EdgeProblemDetails(
    string Code,
    string Title,
    int Status,
    string Detail,
    string CorrelationId);

public sealed record SessionTokensResponse(string AccessToken, DateTime ExpiresAt);
