using System.Globalization;

internal static class CockpitComposition
{
    internal sealed record CalendarWindow(int Year, int Month, DateOnly Start, DateOnly End);

    // Resolves the authoritative Journal Day from a User's timezone and rollover. This is a
    // deliberate re-implementation of journal-service JournalDay.Resolve: ADR-0001 forbids a
    // shared kernel across service boundaries, so both services own the same pure date math.
    // The Journal Day tests assert the identical behavior matrix in both services so drift is
    // caught. Invalid persisted preferences throw rather than silently substituting UTC.
    internal static DateOnly ResolveJournalDay(string timezoneId, string rollover, DateTimeOffset nowUtc)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timezoneId, out var zone))
            throw new ArgumentException("invalid_timezone", nameof(timezoneId));
        if (!TimeOnly.TryParseExact(rollover, "HH:mm", out var rolloverTime))
            throw new ArgumentException("invalid_rollover", nameof(rollover));

        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime);
        var boundary = BoundaryUtc(localDate, rolloverTime, zone);
        return nowUtc.UtcDateTime < boundary ? localDate.AddDays(-1) : localDate;
    }

    private static DateTime BoundaryUtc(DateOnly date, TimeOnly rollover, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(rollover, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).UtcDateTime;
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
