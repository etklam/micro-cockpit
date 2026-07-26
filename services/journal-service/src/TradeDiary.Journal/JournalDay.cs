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
