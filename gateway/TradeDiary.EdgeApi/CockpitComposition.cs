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

    internal static async Task<CompositionResult<CalendarResponse>> CalendarAsync(
        CalendarWindow window,
        EdgeTransport transport,
        HttpContext context)
    {
        var from = window.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = window.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var observations = await transport.GetAsync<CollectionResponse<MarketObservationDayFact>>(
            "journal", $"/internal/market-observation-day-summary?from={from}&to={to}", context);
        var requiredFailure = RequiredFailure(observations, false);
        if (requiredFailure is not null) return CompositionResult<CalendarResponse>.Fail(requiredFailure);

        var observationsByDate = observations.Value!.Items.ToDictionary(item => item.Date);
        var days = new List<CalendarDayResponse>();
        for (var date = window.Start; date <= window.End; date = date.AddDays(1))
        {
            observationsByDate.TryGetValue(date, out var observation);
            days.Add(new CalendarDayResponse(
                date, observation?.MarketObservationId, observation?.UpdateCount ?? 0, observation?.ReadyForReviewCount));
        }
        return CompositionResult<CalendarResponse>.Success(new CalendarResponse(window.Year, window.Month, days));
    }

    private static CompositionFailure? RequiredFailure<T>(DownstreamResponse<T> response, bool allowNotFound)
    {
        if (response.IsSuccess || allowNotFound && response.StatusCode == StatusCodes.Status404NotFound) return null;
        return new CompositionFailure(response.StatusCode, response.Failure);
    }

}

internal sealed record CompositionFailure(int StatusCode, DownstreamFailure Failure);
internal sealed record CompositionResult<T>(T? Value, CompositionFailure? Failure)
{
    internal static CompositionResult<T> Success(T value) => new(value, null);
    internal static CompositionResult<T> Fail(CompositionFailure failure) => new(default, failure);
}
