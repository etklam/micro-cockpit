public sealed class ReminderRecurrenceTests
{
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
        Assert.NotNull(next.Utc);
    }
}
