public static class DiaryAlertSchedule
{
    public const string ActiveStatus = "active";
    public const string ExpiredStatus = "expired";

    public sealed record Decision(
        DateOnly? NextLocalDate,
        DateTime? NextUtcTrigger,
        DateOnly RecurrenceEndLocalDate,
        string Status,
        string? Error);

    public static Decision Create(DateOnly startLocalDate, TimeOnly localTime, string timezoneId, string repeatMode)
    {
        if (repeatMode is not ("none" or "week" or "month"))
            return Error(startLocalDate, "invalid_repeat_mode");

        var recurrenceEnd = CalculateRecurrenceEnd(startLocalDate, repeatMode);
        if (!TryFindTimeZone(timezoneId, out var timezone))
            return Error(recurrenceEnd, "invalid_timezone");

        return FindOccurrence(startLocalDate, localTime, timezone, recurrenceEnd, rejectInvalidLocalTime: true);
    }

    public static Decision Advance(
        DateOnly currentLocalDate,
        TimeOnly localTime,
        string timezoneId,
        string repeatMode,
        DateOnly recurrenceEndLocalDate)
    {
        if (repeatMode is not ("none" or "week" or "month"))
            return Error(recurrenceEndLocalDate, "invalid_repeat_mode");

        if (repeatMode == "none")
            return Expired(recurrenceEndLocalDate);

        if (!TryFindTimeZone(timezoneId, out var timezone))
            return Error(recurrenceEndLocalDate, "invalid_timezone");

        return FindOccurrence(currentLocalDate.AddDays(1), localTime, timezone, recurrenceEndLocalDate, rejectInvalidLocalTime: false);
    }

    public static DateOnly? NextWeekday(DateOnly candidate, DateOnly end)
    {
        while (candidate <= end && candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            candidate = candidate.AddDays(1);

        return candidate <= end ? candidate : null;
    }

    private static Decision FindOccurrence(
        DateOnly firstCandidate,
        TimeOnly localTime,
        TimeZoneInfo timezone,
        DateOnly recurrenceEndLocalDate,
        bool rejectInvalidLocalTime)
    {
        var candidate = NextWeekday(firstCandidate, recurrenceEndLocalDate);
        while (candidate is not null)
        {
            var local = candidate.Value.ToDateTime(localTime);
            if (!timezone.IsInvalidTime(local))
            {
                return new(
                    candidate,
                    TimeZoneInfo.ConvertTimeToUtc(local, timezone),
                    recurrenceEndLocalDate,
                    ActiveStatus,
                    null);
            }

            if (rejectInvalidLocalTime)
                return Error(recurrenceEndLocalDate, "invalid_local_time");

            candidate = NextWeekday(candidate.Value.AddDays(1), recurrenceEndLocalDate);
        }

        return Expired(recurrenceEndLocalDate);
    }

    private static DateOnly CalculateRecurrenceEnd(DateOnly startLocalDate, string repeatMode) => repeatMode switch
    {
        "week" => startLocalDate.AddDays(((int)DayOfWeek.Friday - (int)startLocalDate.DayOfWeek + 7) % 7),
        "month" => new DateOnly(startLocalDate.Year, startLocalDate.Month, 1).AddMonths(1).AddDays(-1),
        _ => startLocalDate
    };

    private static bool TryFindTimeZone(string timezoneId, out TimeZoneInfo timezone)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(timezoneId))
            {
                timezone = TimeZoneInfo.Utc;
                return false;
            }

            timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            timezone = TimeZoneInfo.Utc;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            timezone = TimeZoneInfo.Utc;
            return false;
        }
    }

    private static Decision Expired(DateOnly recurrenceEndLocalDate) =>
        new(null, null, recurrenceEndLocalDate, ExpiredStatus, null);

    private static Decision Error(DateOnly recurrenceEndLocalDate, string error) =>
        new(null, null, recurrenceEndLocalDate, ExpiredStatus, error);
}
