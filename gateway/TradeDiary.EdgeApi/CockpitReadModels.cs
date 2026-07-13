using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;

// Cockpit is an Edge-owned read model, not a domain model. The module translates service
// responses into the two browser-facing aggregates while leaving write/domain behavior in
// the downstream services.
internal static class CockpitReadModels
{
    internal delegate Task<DownstreamResponse> Fetch(string service, string path);

    internal sealed record DownstreamResponse(int StatusCode, JsonNode? Body);

    internal sealed record ReadResult<T>(T? Model, string? Problem)
    {
        internal IResult ToHttpResult() =>
            Problem is null
                ? Results.Ok(Model)
                : Results.Problem(Problem, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    internal sealed record CalendarWindow(int Year, int Month, DateOnly Start, DateOnly End);

    internal sealed record DashboardReadModel(
        DateOnly LocalDate,
        DashboardDiary Diary,
        DailyPerformanceFact? Performance,
        long? PendingAlerts,
        DisciplineFact? Discipline,
        IReadOnlyList<DiaryFact> RecentDiaries,
        DashboardCapabilities Capabilities);

    internal sealed record DashboardDiary(bool WrittenToday, long Count);

    internal sealed record DashboardCapabilities(string Alerts, string Discipline);

    internal sealed record CalendarReadModel(
        int Year,
        int Month,
        MonthSummaryFact? Summary,
        IReadOnlyList<CalendarDayReadModel> Days,
        CalendarCapabilities Capabilities);

    internal sealed record CalendarDayReadModel(
        DateOnly Date,
        DailyPerformanceFact? Performance,
        long DiaryCount,
        long TransactionCount,
        long? AlertCount);

    internal sealed record CalendarCapabilities(string Alerts);

    // These are local facts used by the read model. They deliberately do not become service
    // DTOs or a shared package; each service still owns its API and persistence contract.
    internal sealed record DiaryFact(
        Guid Id,
        DateOnly LocalDate,
        string Title,
        string Content,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    internal sealed record DiaryDayFact(DateOnly LocalDate, long DiaryCount, long TransactionCount);

    internal sealed record DailyPerformanceFact(
        DateOnly LocalDate,
        decimal PnlAmount,
        decimal? CapitalBase,
        decimal? PnlPercent,
        string Note);

    internal sealed record MonthSummaryFact(
        int Year,
        int Month,
        decimal Total,
        long RecordedDays,
        long ProfitDays,
        long LossDays,
        long FlatDays,
        decimal? BestDay,
        decimal? WorstDay);

    internal sealed record DisciplineFact(
        Guid Id,
        string Content,
        int Position,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private sealed record DayAlertFact(DateOnly Date, long Count);
    private sealed record CalendarAlertFact(DateOnly LocalDate, long Count);

    private static readonly JsonSerializerOptions DownstreamJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    internal static DateOnly ResolveLocalDate(ClaimsPrincipal user, DateTimeOffset nowUtc)
    {
        var timezoneId = user.FindFirst("timezone")?.Value ?? "UTC";
        TimeZoneInfo timezone;
        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch
        {
            // Keep the existing Edge behavior: a bad claim must not make the dashboard fail.
            timezone = TimeZoneInfo.Utc;
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timezone).DateTime);
    }

    internal static bool TryCreateCalendarWindow(int year, int month, out CalendarWindow window)
    {
        if (month is < 1 or > 12)
        {
            window = default!;
            return false;
        }

        try
        {
            var start = new DateOnly(year, month, 1);
            window = new CalendarWindow(year, month, start, start.AddMonths(1).AddDays(-1));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            window = default!;
            return false;
        }
    }

    internal static async Task<ReadResult<DashboardReadModel>> ReadDashboardAsync(
        DateOnly localDate,
        Fetch fetch)
    {
        var date = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var tasks = new[]
        {
            FetchSafely(fetch, "journal", $"/internal/diary-day-summary?from={date}&to={date}"),
            FetchSafely(fetch, "journal", "/internal/diaries"),
            FetchSafely(fetch, "performance", $"/internal/performance/day/{date}"),
            FetchSafely(fetch, "discipline", $"/internal/disciplines/today?date={date}"),
            FetchSafely(fetch, "reminder", $"/internal/diary-alerts/day-summary?date={date}")
        };

        await Task.WhenAll(tasks);
        var journalDay = tasks[0].Result;
        var diaries = tasks[1].Result;
        var performance = tasks[2].Result;
        var discipline = tasks[3].Result;
        var alerts = tasks[4].Result;

        if (!CockpitStatusPolicy.DashboardRequired(journalDay, diaries, performance))
            return Unavailable<DashboardReadModel>("Required dashboard service unavailable.");
        if (CockpitStatusPolicy.OptionalAuthorizationFailure(discipline) ||
            CockpitStatusPolicy.OptionalAuthorizationFailure(alerts))
            return Unavailable<DashboardReadModel>("Downstream authorization failed.");

        var journalFact = ReadItems<DiaryDayFact>(journalDay.Body).FirstOrDefault();
        var recentDiaries = ReadItems<DiaryFact>(diaries.Body).Take(5).ToArray();
        var performanceFact = performance.StatusCode == StatusCodes.Status200OK
            ? ReadFact<DailyPerformanceFact>(performance.Body)
            : null;
        var disciplineFact = discipline.StatusCode == StatusCodes.Status200OK
            ? ReadFact<DisciplineFact>(discipline.Body)
            : null;
        var alertFact = alerts.StatusCode == StatusCodes.Status200OK
            ? ReadFact<DayAlertFact>(alerts.Body)
            : null;

        return Success(new DashboardReadModel(
            localDate,
            new DashboardDiary(journalFact?.DiaryCount > 0, journalFact?.DiaryCount ?? 0),
            performanceFact,
            alerts.StatusCode == StatusCodes.Status200OK ? alertFact?.Count : null,
            disciplineFact,
            recentDiaries,
            new DashboardCapabilities(
                alerts.StatusCode == StatusCodes.Status200OK ? "available" : "unavailable",
                CockpitStatusPolicy.DisciplineCapability(discipline))));
    }

    internal static async Task<ReadResult<CalendarReadModel>> ReadCalendarAsync(
        CalendarWindow window,
        Fetch fetch)
    {
        var from = window.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = window.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var tasks = new[]
        {
            FetchSafely(fetch, "journal", $"/internal/diary-day-summary?from={from}&to={to}"),
            FetchSafely(fetch, "performance", $"/internal/daily-performances?from={from}&to={to}"),
            FetchSafely(fetch, "performance", $"/internal/performance/month-summary?year={window.Year}&month={window.Month}"),
            FetchSafely(fetch, "reminder", $"/internal/diary-alerts/day-summaries?from={from}&to={to}")
        };

        await Task.WhenAll(tasks);
        var journal = tasks[0].Result;
        var performance = tasks[1].Result;
        var summary = tasks[2].Result;
        var alerts = tasks[3].Result;

        if (!CockpitStatusPolicy.CalendarRequired(journal, performance, summary))
            return Unavailable<CalendarReadModel>("Required calendar service unavailable.");
        if (CockpitStatusPolicy.OptionalAuthorizationFailure(alerts))
            return Unavailable<CalendarReadModel>("Downstream authorization failed.");

        var journalByDate = IndexByDate(ReadItems<DiaryDayFact>(journal.Body), fact => fact.LocalDate);
        var performanceByDate = IndexByDate(ReadItems<DailyPerformanceFact>(performance.Body), fact => fact.LocalDate);
        var alertByDate = IndexByDate(ReadItems<CalendarAlertFact>(alerts.Body), fact => fact.LocalDate);
        var days = new List<CalendarDayReadModel>(window.End.DayNumber - window.Start.DayNumber + 1);
        for (var offset = 0; offset <= window.End.DayNumber - window.Start.DayNumber; offset++)
        {
            var date = window.Start.AddDays(offset);
            journalByDate.TryGetValue(date, out var journalFact);
            performanceByDate.TryGetValue(date, out var performanceFact);
            alertByDate.TryGetValue(date, out var alertFact);
            days.Add(new CalendarDayReadModel(
                date,
                performanceFact,
                journalFact?.DiaryCount ?? 0,
                journalFact?.TransactionCount ?? 0,
                alerts.StatusCode == StatusCodes.Status200OK ? alertFact?.Count ?? 0 : null));
        }

        return Success(new CalendarReadModel(
            window.Year,
            window.Month,
            summary.StatusCode == StatusCodes.Status200OK ? ReadFact<MonthSummaryFact>(summary.Body) : null,
            days,
            new CalendarCapabilities(alerts.StatusCode == StatusCodes.Status200OK ? "available" : "unavailable")));
    }

    private static async Task<DownstreamResponse> FetchSafely(Fetch fetch, string service, string path)
    {
        try
        {
            return await fetch(service, path);
        }
        catch
        {
            return new DownstreamResponse(StatusCodes.Status503ServiceUnavailable, null);
        }
    }

    private static ReadResult<T> Success<T>(T model) => new(model, null);

    private static ReadResult<T> Unavailable<T>(string problem) => new(default, problem);

    private static T? ReadFact<T>(JsonNode? node) where T : class
    {
        if (node is null) return null;
        try
        {
            return node.Deserialize<T>(DownstreamJson);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static IReadOnlyList<T> ReadItems<T>(JsonNode? body) where T : class
    {
        if (body?["items"] is not JsonArray items) return [];
        return items.Select(ReadFact<T>).OfType<T>().ToArray();
    }

    private static Dictionary<DateOnly, T> IndexByDate<T>(IEnumerable<T> facts, Func<T, DateOnly> date)
    {
        var index = new Dictionary<DateOnly, T>();
        foreach (var fact in facts) index[date(fact)] = fact;
        return index;
    }

    private static class CockpitStatusPolicy
    {
        internal static bool DashboardRequired(
            DownstreamResponse journalDay,
            DownstreamResponse diaries,
            DownstreamResponse performance) =>
            journalDay.StatusCode == StatusCodes.Status200OK &&
            diaries.StatusCode == StatusCodes.Status200OK &&
            performance.StatusCode is StatusCodes.Status200OK or StatusCodes.Status404NotFound;

        internal static bool CalendarRequired(
            DownstreamResponse journal,
            DownstreamResponse performance,
            DownstreamResponse summary) =>
            journal.StatusCode == StatusCodes.Status200OK &&
            performance.StatusCode == StatusCodes.Status200OK &&
            summary.StatusCode == StatusCodes.Status200OK;

        internal static bool OptionalAuthorizationFailure(DownstreamResponse response) =>
            response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden;

        internal static string DisciplineCapability(DownstreamResponse response) => response.StatusCode switch
        {
            StatusCodes.Status200OK => "available",
            StatusCodes.Status404NotFound => "empty",
            _ => "unavailable"
        };
    }
}
