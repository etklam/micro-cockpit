using System.Globalization;
using System.Security.Claims;

internal static class CockpitComposition
{
    internal sealed record CalendarWindow(int Year, int Month, DateOnly Start, DateOnly End);

    internal static DateOnly ResolveLocalDate(ClaimsPrincipal user, DateTimeOffset nowUtc)
    {
        var timezoneId = user.FindFirst("timezone")?.Value ?? "UTC";
        try
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, TimeZoneInfo.FindSystemTimeZoneById(timezoneId)).DateTime);
        }
        catch
        {
            return DateOnly.FromDateTime(nowUtc.UtcDateTime);
        }
    }

    internal static DateOnly ResolveLocalDate(string timezoneId, DateTimeOffset nowUtc)
    {
        try
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, TimeZoneInfo.FindSystemTimeZoneById(timezoneId)).DateTime);
        }
        catch
        {
            return DateOnly.FromDateTime(nowUtc.UtcDateTime);
        }
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

    internal static async Task<CompositionResult<DashboardResponse>> DashboardAsync(
        DateOnly localDate,
        EdgeTransport transport,
        HttpContext context)
    {
        var date = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var journalDayTask = transport.GetAsync<CollectionResponse<DiaryDayFact>>("journal", $"/internal/diary-day-summary?from={date}&to={date}", context);
        var diariesTask = transport.GetAsync<DiaryPageResponse>("journal", "/internal/diaries?limit=5", context);
        var performanceTask = transport.GetAsync<DailyPerformanceResponse>("performance", $"/internal/performance/day/{date}", context);
        var disciplineTask = transport.GetAsync<DisciplineResponse>("discipline", $"/internal/disciplines/today?date={date}", context);
        var alertsTask = transport.GetAsync<DayAlertFact>("reminder", $"/internal/diary-alerts/day-summary?date={date}", context);
        await Task.WhenAll(journalDayTask, diariesTask, performanceTask, disciplineTask, alertsTask);

        var journalDay = await journalDayTask;
        var diaries = await diariesTask;
        var performance = await performanceTask;
        var discipline = await disciplineTask;
        var alerts = await alertsTask;

        var requiredFailure = RequiredFailure(journalDay, allowNotFound: false) ??
                              RequiredFailure(diaries, allowNotFound: false) ??
                              RequiredFailure(performance, allowNotFound: true);
        if (requiredFailure is not null) return CompositionResult<DashboardResponse>.Fail(requiredFailure);

        var authorizationFailure = AuthorizationFailure(discipline) ?? AuthorizationFailure(alerts);
        if (authorizationFailure is not null) return CompositionResult<DashboardResponse>.Fail(authorizationFailure);

        var journalFact = journalDay.Value!.Items.FirstOrDefault();
        return CompositionResult<DashboardResponse>.Success(new DashboardResponse(
            localDate,
            new DashboardDiaryResponse(journalFact?.DiaryCount > 0, journalFact?.DiaryCount ?? 0),
            performance.StatusCode == StatusCodes.Status200OK ? performance.Value : null,
            alerts.StatusCode == StatusCodes.Status200OK ? alerts.Value?.Count : null,
            discipline.StatusCode == StatusCodes.Status200OK ? discipline.Value : null,
            diaries.Value!.Items.Take(5).ToArray(),
            new DashboardCapabilitiesResponse(Capability(alerts), Capability(discipline, notFoundIsEmpty: true))));
    }

    internal static async Task<CompositionResult<CalendarResponse>> CalendarAsync(
        CalendarWindow window,
        EdgeTransport transport,
        HttpContext context)
    {
        var from = window.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = window.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var journalTask = transport.GetAsync<CollectionResponse<DiaryDayFact>>("journal", $"/internal/diary-day-summary?from={from}&to={to}", context);
        var performanceTask = transport.GetAsync<CollectionResponse<DailyPerformanceResponse>>("performance", $"/internal/daily-performances?from={from}&to={to}", context);
        var summaryTask = transport.GetAsync<MonthSummaryResponse>("performance", $"/internal/performance/month-summary?year={window.Year}&month={window.Month}", context);
        var alertsTask = transport.GetAsync<CollectionResponse<CalendarAlertFact>>("reminder", $"/internal/diary-alerts/day-summaries?from={from}&to={to}", context);
        await Task.WhenAll(journalTask, performanceTask, summaryTask, alertsTask);

        var journal = await journalTask;
        var performance = await performanceTask;
        var summary = await summaryTask;
        var alerts = await alertsTask;
        var requiredFailure = RequiredFailure(journal, false) ?? RequiredFailure(performance, false) ?? RequiredFailure(summary, false);
        if (requiredFailure is not null) return CompositionResult<CalendarResponse>.Fail(requiredFailure);
        var authorizationFailure = AuthorizationFailure(alerts);
        if (authorizationFailure is not null) return CompositionResult<CalendarResponse>.Fail(authorizationFailure);

        var journalByDate = journal.Value!.Items.ToDictionary(item => item.LocalDate);
        var performanceByDate = performance.Value!.Items.ToDictionary(item => item.LocalDate);
        var alertsByDate = alerts.Value?.Items.ToDictionary(item => item.LocalDate) ?? [];
        var days = new List<CalendarDayResponse>();
        for (var date = window.Start; date <= window.End; date = date.AddDays(1))
        {
            journalByDate.TryGetValue(date, out var journalFact);
            performanceByDate.TryGetValue(date, out var performanceFact);
            alertsByDate.TryGetValue(date, out var alertFact);
            days.Add(new CalendarDayResponse(
                date,
                performanceFact,
                journalFact?.DiaryCount ?? 0,
                journalFact?.TransactionCount ?? 0,
                alerts.StatusCode == StatusCodes.Status200OK ? alertFact?.Count ?? 0 : null));
        }

        return CompositionResult<CalendarResponse>.Success(new CalendarResponse(
            window.Year,
            window.Month,
            summary.Value,
            days,
            new CalendarCapabilitiesResponse(Capability(alerts))));
    }

    internal static async Task<CompositionResult<StockPageResponse>> StockPageAsync(
        string symbol,
        EdgeTransport transport,
        HttpContext context)
    {
        var escaped = Uri.EscapeDataString(symbol);
        var stockTask = transport.GetAsync<StockResponse>("stock-research", $"/internal/stocks/{escaped}", context);
        var barsTask = transport.GetAsync<BarsResponse>("market-data", $"/internal/v1/bars/{escaped}{context.Request.QueryString}", context);
        await Task.WhenAll(stockTask, barsTask);
        var stock = await stockTask;
        var bars = await barsTask;
        var requiredFailure = RequiredFailure(stock, false);
        if (requiredFailure is not null) return CompositionResult<StockPageResponse>.Fail(requiredFailure);
        var authorizationFailure = AuthorizationFailure(bars);
        if (authorizationFailure is not null) return CompositionResult<StockPageResponse>.Fail(authorizationFailure);
        return CompositionResult<StockPageResponse>.Success(new StockPageResponse(
            stock.Value!,
            bars.StatusCode == StatusCodes.Status200OK && bars.Failure == DownstreamFailure.None ? bars.Value : null,
            new StockPageCapabilitiesResponse(Capability(bars))));
    }

    private static CompositionFailure? RequiredFailure<T>(DownstreamResponse<T> response, bool allowNotFound)
    {
        if (response.IsSuccess || allowNotFound && response.StatusCode == StatusCodes.Status404NotFound) return null;
        return new CompositionFailure(response.StatusCode, response.Failure);
    }

    private static CompositionFailure? AuthorizationFailure<T>(DownstreamResponse<T> response) =>
        response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
            ? new CompositionFailure(response.StatusCode, response.Failure)
            : null;

    private static CapabilityStatus Capability<T>(DownstreamResponse<T> response, bool notFoundIsEmpty = false)
    {
        if (response.IsSuccess) return CapabilityStatus.Available;
        if (notFoundIsEmpty && response.StatusCode == StatusCodes.Status404NotFound) return CapabilityStatus.Empty;
        return CapabilityStatus.Unavailable;
    }
}

internal sealed record CompositionFailure(int StatusCode, DownstreamFailure Failure);
internal sealed record CompositionResult<T>(T? Value, CompositionFailure? Failure)
{
    internal static CompositionResult<T> Success(T value) => new(value, null);
    internal static CompositionResult<T> Fail(CompositionFailure failure) => new(default, failure);
}
