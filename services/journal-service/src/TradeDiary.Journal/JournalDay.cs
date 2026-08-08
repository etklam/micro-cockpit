public static class JournalDay
{
    public static DateOnly Resolve(DateTimeOffset instant, string timezone, string rollover)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out var zone))
            throw new ArgumentException("invalid_timezone", nameof(timezone));
        if (!TimeOnly.TryParseExact(rollover, "HH:mm", out var rolloverTime))
            throw new ArgumentException("invalid_rollover", nameof(rollover));

        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
        var boundary = BoundaryUtc(localDate, rolloverTime, zone);
        return instant.UtcDateTime < boundary ? localDate.AddDays(-1) : localDate;
    }

    public static bool TryUtcWindow(
        DateOnly from,
        DateOnly to,
        string timezone,
        string rollover,
        int maxDays,
        out DateTime startUtc,
        out DateTime endUtc)
    {
        startUtc = default;
        endUtc = default;
        if (to < from || to == DateOnly.MaxValue || to.DayNumber - from.DayNumber + 1 > maxDays)
            return false;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out var zone)
            || !TimeOnly.TryParseExact(rollover, "HH:mm", out var rolloverTime))
            return false;

        startUtc = BoundaryUtc(from, rolloverTime, zone);
        endUtc = BoundaryUtc(to.AddDays(1), rolloverTime, zone);
        return true;
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
}
