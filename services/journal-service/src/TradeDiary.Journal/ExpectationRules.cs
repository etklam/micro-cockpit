using Npgsql;

static class ExpectationRules
{
    private const int MaxTextLength = 2000;
    private const int MaxMarketLength = 32;
    private static readonly TimeZoneInfo UsEastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    internal static string? Validate(ExpectationWrite input, DateTimeOffset now, out NormalizedExpectation normalized)
    {
        normalized = default!;
        var expectedBehavior = input.ExpectedBehavior?.Trim();
        var invalidationCondition = input.InvalidationCondition?.Trim();
        var market = input.Market?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(expectedBehavior)) return "expected_behavior_required";
        if (expectedBehavior.Length > MaxTextLength) return "expected_behavior_too_long";
        if (string.IsNullOrWhiteSpace(invalidationCondition)) return "invalidation_condition_required";
        if (invalidationCondition.Length > MaxTextLength) return "invalidation_condition_too_long";
        if (string.IsNullOrWhiteSpace(market) || market.Length > MaxMarketLength) return "invalid_market";
        if (input.Deadline is not null && input.DeadlinePreset is not null) return "deadline_or_preset_required";
        if (input.Deadline is null && input.DeadlinePreset is null) return "deadline_required";

        DateTimeOffset deadline;
        if (input.DeadlinePreset is { } preset)
        {
            if (market != "US") return "trading_day_preset_unavailable";
            deadline = ResolveUsTradingDayDeadline(now, preset == ExpectationDeadlinePreset.next_trading_day ? 1 : 5);
        }
        else deadline = input.Deadline!.Value.ToUniversalTime();

        normalized = new(expectedBehavior, deadline, invalidationCondition, input.Confidence, market);
        return null;
    }

    internal static ExpectationResponse Read(NpgsqlDataReader reader, DateTimeOffset now)
    {
        var deadline = DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc);
        DateTime? invalidatedAt = reader.IsDBNull(9) ? null : DateTime.SpecifyKind(reader.GetDateTime(9), DateTimeKind.Utc);
        var elapsed = deadline <= now.UtcDateTime;
        var reviewed = reader.FieldCount > 12 && reader.GetBoolean(12);
        return new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetFieldValue<DateOnly>(3), reader.GetString(4), deadline,
            reader.GetString(6), Enum.Parse<ExpectationConfidence>(reader.GetString(7)), reader.GetString(8), invalidatedAt,
            reviewed ? ExpectationReadiness.reviewed : invalidatedAt is not null || elapsed ? ExpectationReadiness.ready_for_review : ExpectationReadiness.active,
            elapsed, DateTime.SpecifyKind(reader.GetDateTime(10), DateTimeKind.Utc), DateTime.SpecifyKind(reader.GetDateTime(11), DateTimeKind.Utc));
    }

    internal static ExpectationEditResponse Edit(ExpectationResponse response, bool honestyReminderRequired) => new(
        response.Id, response.ObservationUpdateId, response.MarketObservationId, response.JournalDay, response.ExpectedBehavior,
        response.Deadline, response.InvalidationCondition, response.Confidence, response.Market, response.InvalidatedAt,
        response.Readiness, response.DeadlineElapsed, response.CreatedAt, response.UpdatedAt, honestyReminderRequired);

    private static DateTimeOffset ResolveUsTradingDayDeadline(DateTimeOffset now, int tradingDays)
    {
        var local = TimeZoneInfo.ConvertTime(now, UsEastern);
        var date = DateOnly.FromDateTime(local.DateTime);
        while (tradingDays > 0)
        {
            date = date.AddDays(1);
            if (IsUsTradingDay(date)) tradingDays--;
        }
        var close = date.ToDateTime(new TimeOnly(16, 0), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(close, UsEastern);
    }

    private static bool IsUsTradingDay(DateOnly date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        var holidays = Holidays(date.Year);
        if (Observed(new DateOnly(date.Year + 1, 1, 1)) == date) return false;
        return !holidays.Contains(date);
    }

    private static HashSet<DateOnly> Holidays(int year)
    {
        var easter = EasterSunday(year);
        return
        [
            Observed(new DateOnly(year, 1, 1)),
            NthWeekday(year, 1, DayOfWeek.Monday, 3),
            NthWeekday(year, 2, DayOfWeek.Monday, 3),
            easter.AddDays(-2),
            LastWeekday(year, 5, DayOfWeek.Monday),
            Observed(new DateOnly(year, 6, 19)),
            Observed(new DateOnly(year, 7, 4)),
            NthWeekday(year, 9, DayOfWeek.Monday, 1),
            NthWeekday(year, 11, DayOfWeek.Thursday, 4),
            Observed(new DateOnly(year, 12, 25)),
        ];
    }

    private static DateOnly Observed(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Saturday => date.AddDays(-1),
        DayOfWeek.Sunday => date.AddDays(1),
        _ => date,
    };

    private static DateOnly NthWeekday(int year, int month, DayOfWeek weekday, int occurrence)
    {
        var date = new DateOnly(year, month, 1);
        var offset = ((int)weekday - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(offset + (occurrence - 1) * 7);
    }

    private static DateOnly LastWeekday(int year, int month, DayOfWeek weekday)
    {
        var date = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return date.AddDays(-(((int)date.DayOfWeek - (int)weekday + 7) % 7));
    }

    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }
}

sealed record NormalizedExpectation(
    string ExpectedBehavior,
    DateTimeOffset Deadline,
    string InvalidationCondition,
    ExpectationConfidence Confidence,
    string Market);
