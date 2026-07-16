public static class DiaryReviewRules
{
    public static readonly IReadOnlySet<string> Emotions = new HashSet<string>
    {
        "calm", "confident", "uncertain", "anxious", "fomo", "frustrated", "overconfident", "other"
    };

    public static readonly IReadOnlySet<string> ProcessAssessments = new HashSet<string> { "good", "mixed", "poor" };

    public static readonly IReadOnlySet<string> MistakeTags = new HashSet<string>
    {
        "no_plan", "fomo", "poor_timing", "risk_violation", "overtrading", "ignored_signal",
        "early_exit", "late_exit", "other"
    };

    public static string? Validate(DiaryReviewWrite input)
    {
        if (input.DisciplineScore is < 1 or > 5) return "invalid_discipline_score";
        if (input.ExecutionScore is < 1 or > 5) return "invalid_execution_score";
        if (input.Emotion is not null && !Emotions.Contains(input.Emotion)) return "invalid_emotion";
        if (input.ProcessAssessment is not null && !ProcessAssessments.Contains(input.ProcessAssessment)) return "invalid_process_assessment";
        var mistakeTags = input.MistakeTags ?? [];
        if (mistakeTags.Count != mistakeTags.Distinct(StringComparer.Ordinal).Count()) return "duplicate_mistake_tag";
        if (mistakeTags.Any(tag => !MistakeTags.Contains(tag))) return "invalid_mistake_tag";
        return null;
    }

    public static bool InvalidRange(DateOnly from, DateOnly to) => to < from || to.DayNumber - from.DayNumber > 365;
}

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
