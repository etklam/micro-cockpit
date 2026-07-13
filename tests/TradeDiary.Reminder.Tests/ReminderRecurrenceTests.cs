public sealed class ReminderRecurrenceTests
{
    [Fact]
    public void Create_none_returns_the_start_occurrence_and_active_status()
    {
        var decision = DiaryAlertSchedule.Create(
            new DateOnly(2026, 7, 13), new TimeOnly(9, 0), "Asia/Taipei", "none");

        Assert.Equal(new DateOnly(2026, 7, 13), decision.NextLocalDate);
        Assert.Equal(new DateTime(2026, 7, 13, 1, 0, 0, DateTimeKind.Utc), decision.NextUtcTrigger);
        Assert.Equal(new DateOnly(2026, 7, 13), decision.RecurrenceEndLocalDate);
        Assert.Equal("active", decision.Status);
        Assert.Null(decision.Error);
    }

    [Fact]
    public void Advance_none_is_terminal_after_delivery()
    {
        var decision = DiaryAlertSchedule.Advance(
            new DateOnly(2026, 7, 13), new TimeOnly(9, 0), "Asia/Taipei", "none", new DateOnly(2026, 7, 13));

        Assert.Null(decision.NextLocalDate);
        Assert.Null(decision.NextUtcTrigger);
        Assert.Equal(new DateOnly(2026, 7, 13), decision.RecurrenceEndLocalDate);
        Assert.Equal("expired", decision.Status);
        Assert.Null(decision.Error);
    }

    [Fact]
    public void Create_week_ends_on_friday_and_uses_the_first_occurrence()
    {
        var decision = DiaryAlertSchedule.Create(
            new DateOnly(2026, 7, 14), new TimeOnly(9, 0), "Etc/UTC", "week");

        Assert.Equal(new DateOnly(2026, 7, 14), decision.NextLocalDate);
        Assert.Equal(new DateTime(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc), decision.NextUtcTrigger);
        Assert.Equal(new DateOnly(2026, 7, 17), decision.RecurrenceEndLocalDate);
        Assert.Equal("active", decision.Status);
        Assert.Null(decision.Error);
    }

    [Fact]
    public void Create_week_skips_a_weekend_start()
    {
        var decision = DiaryAlertSchedule.Create(
            new DateOnly(2026, 7, 11), new TimeOnly(9, 0), "Etc/UTC", "week");

        Assert.Equal(new DateOnly(2026, 7, 13), decision.NextLocalDate);
        Assert.Equal(new DateOnly(2026, 7, 17), decision.RecurrenceEndLocalDate);
        Assert.Equal("active", decision.Status);
    }

    [Fact]
    public void Create_month_starts_at_the_first_valid_weekday_on_or_after_start()
    {
        var decision = DiaryAlertSchedule.Create(
            new DateOnly(2026, 7, 30), new TimeOnly(9, 0), "Etc/UTC", "month");

        Assert.Equal(new DateOnly(2026, 7, 30), decision.NextLocalDate);
        Assert.Equal(new DateOnly(2026, 7, 31), decision.RecurrenceEndLocalDate);
        Assert.Equal("active", decision.Status);
    }

    [Fact]
    public void Advance_month_returns_the_last_occurrence_then_expires()
    {
        var next = DiaryAlertSchedule.Advance(
            new DateOnly(2026, 7, 30), new TimeOnly(9, 0), "Etc/UTC", "month", new DateOnly(2026, 7, 31));
        var terminal = DiaryAlertSchedule.Advance(
            new DateOnly(2026, 7, 31), new TimeOnly(9, 0), "Etc/UTC", "month", new DateOnly(2026, 7, 31));

        Assert.Equal(new DateOnly(2026, 7, 31), next.NextLocalDate);
        Assert.Equal("active", next.Status);
        Assert.Equal(new DateOnly(2026, 7, 31), terminal.RecurrenceEndLocalDate);
        Assert.Null(terminal.NextLocalDate);
        Assert.Null(terminal.NextUtcTrigger);
        Assert.Equal("expired", terminal.Status);
        Assert.Null(terminal.Error);
    }

    [Fact]
    public void Create_rejects_an_unknown_timezone()
    {
        var decision = DiaryAlertSchedule.Create(
            new DateOnly(2026, 7, 13), new TimeOnly(9, 0), "Not/A_Timezone", "none");

        Assert.Equal("invalid_timezone", decision.Error);
        Assert.Equal("expired", decision.Status);
    }

    [Fact]
    public void Advance_recomputes_utc_from_the_next_occurrence_wall_clock()
    {
        var decision = DiaryAlertSchedule.Advance(
            new DateOnly(2026, 10, 30), new TimeOnly(9, 0), "America/New_York", "month", new DateOnly(2026, 11, 3));

        Assert.Equal(new DateOnly(2026, 11, 2), decision.NextLocalDate);
        Assert.Equal(new DateTime(2026, 11, 2, 14, 0, 0, DateTimeKind.Utc), decision.NextUtcTrigger);
        Assert.Equal("active", decision.Status);
    }

    [Fact]
    public void Next_weekday_skips_weekends()
    {
        Assert.Equal(new DateOnly(2026, 7, 13), ReminderEngine.NextWeekday(new DateOnly(2026, 7, 11), new DateOnly(2026, 7, 20)));
    }

    [Fact]
    public void Next_weekday_returns_null_after_the_recurrence_end()
    {
        Assert.Null(ReminderEngine.NextWeekday(new DateOnly(2026, 7, 18), new DateOnly(2026, 7, 19)));
    }

    [Fact]
    public void Recurrence_skips_a_nonexistent_dst_local_time()
    {
        var next = ReminderEngine.CalculateNextOccurrence(
            new DateOnly(2026, 3, 6), new TimeOnly(2, 30), "America/New_York", "month", new DateOnly(2026, 3, 31));

        Assert.Equal(new DateOnly(2026, 3, 9), next.LocalDate);
        Assert.Equal(new DateTime(2026, 3, 9, 6, 30, 0, DateTimeKind.Utc), next.Utc);
    }
}
