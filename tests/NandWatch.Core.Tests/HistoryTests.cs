using NandWatch.Core.Query;

namespace NandWatch.Core.Tests;

public class WriteHistoryTests
{
    private static readonly DateOnly D1 = new(2026, 10, 1);

    /// <summary>A reading at the given local day and hour (UTC offset 0 keeps the arithmetic plain).</summary>
    private static CounterPoint P(int day, int hour, long value, long? poh = null) =>
        new(new DateTimeOffset(D1.AddDays(day - 1).ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero), D1.AddDays(day - 1), value, poh);

    [Fact]
    public void Hourly_readings_add_up_by_day_and_midnight_files_under_the_later_day()
    {
        var s = WriteHistory.Daily([P(1, 21, 100), P(1, 22, 110), P(1, 23, 130), P(2, 0, 135), P(2, 1, 150)], D1, D1.AddDays(1));
        Assert.Equal([30L, 20L], s.Days.Select(d => d.Units!.Value));
        Assert.All(s.Days, d => Assert.Equal(DayState.Known, d.State));
        Assert.All(s.Days, d => Assert.False(d.IncludesGap));
    }

    [Fact]
    public void Days_the_drive_spent_off_are_zero_not_unknown()
    {
        // Shut down on day 1 evening, back on day 4: power-on hours barely moved.
        var s = WriteHistory.Daily([P(1, 22, 100, 500), P(4, 9, 104, 501)], D1, D1.AddDays(3));
        Assert.Equal([DayState.Known, DayState.Off, DayState.Off, DayState.Known], s.Days.Select(d => d.State));
        Assert.Equal(0L, s.Days[1].Units);
        Assert.Equal(4L, s.Days[3].Units);
        Assert.False(s.Days[3].IncludesGap);   // it was off: nothing hidden in day 4's total
    }

    [Fact]
    public void Days_the_drive_ran_with_nothing_collecting_are_unknown()
    {
        // Power-on hours grew by 40: the PC ran, the collector did not.
        var s = WriteHistory.Daily([P(1, 22, 100, 500), P(4, 9, 400, 540)], D1, D1.AddDays(3));
        Assert.Equal([DayState.Known, DayState.Unknown, DayState.Unknown, DayState.Known], s.Days.Select(d => d.State));
        Assert.Null(s.Days[1].Units);
        Assert.Equal(300L, s.Days[3].Units);
        Assert.True(s.Days[3].IncludesGap);    // days 2 and 3's writes, filed on day 4
    }

    [Fact]
    public void A_gap_without_power_on_hours_is_unknown_not_assumed_off()
    {
        var s = WriteHistory.Daily([P(1, 22, 100), P(3, 9, 100)], D1, D1.AddDays(2));
        Assert.Equal(DayState.Unknown, s.Days[1].State);
    }

    [Fact]
    public void A_counter_going_backwards_is_a_reset_never_negative_use()
    {
        var s = WriteHistory.Daily([P(1, 10, 1000), P(1, 11, 5), P(1, 12, 25)], D1, D1);
        Assert.Equal(20L, s.Days[0].Units);
        var reset = Assert.Single(s.Resets);
        Assert.Equal((1000L, 5L), (reset.From, reset.To));
    }

    [Fact]
    public void Days_before_the_first_reading_and_after_the_last_are_unknown()
    {
        var s = WriteHistory.Daily([P(2, 10, 0), P(2, 11, 10)], D1, D1.AddDays(2));
        Assert.Equal([DayState.Unknown, DayState.Known, DayState.Unknown], s.Days.Select(d => d.State));
    }
}

public class ProjectionTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A reading a given number of days ago with that many TB written (1 TB = 1,953,125 units).</summary>
    private static CounterPoint Tb(double daysAgo, decimal tb)
    {
        var at = Now.AddDays(-daysAgo);
        return new CounterPoint(at, DateOnly.FromDateTime(at.UtcDateTime), (long)(tb * 1_953_125), null);
    }

    [Fact]
    public void Less_than_fourteen_days_of_history_gives_no_rate()
    {
        Assert.Null(Projection.Rate([Tb(10, 40), Tb(0, 41)], Now, TimeSpan.FromDays(30)));
    }

    [Fact]
    public void The_rate_is_measured_from_the_reading_before_the_window_starts()
    {
        var rate = Projection.Rate([Tb(100, 30), Tb(31, 39), Tb(15, 40), Tb(0, 42)], Now, TimeSpan.FromDays(30))!;
        Assert.Equal(31, rate.SpanDays, 3);
        Assert.Equal(3m / 31m, rate.TbPerDay, 6);
    }

    [Fact]
    public void Two_windows_give_two_end_dates_and_the_two_life_figures_stay_apart()
    {
        // 0.1 TB a day for the last 30 days, 0.05 before; 105 TB of the 150 remain.
        var points = new[] { Tb(90, 39), Tb(30, 42), Tb(0, 45) };
        var e = Projection.Estimate(points, vendorPercentUsed: 21, ratedTbw: 150, Now, Utc);
        Assert.Equal(45m, e.WrittenTb);
        Assert.Equal(30m, e.UsedByWritesPercent);
        Assert.Equal(21, e.VendorPercentUsed);
        Assert.Equal(DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1050), e.End30);   // 105 TB at 0.1 a day
        Assert.Equal(DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1575), e.End90);   // at 6 TB over 90 days
    }

    [Fact]
    public void No_rating_means_no_end_date_rather_than_a_guess()
    {
        var e = Projection.Estimate([Tb(60, 1), Tb(0, 2)], null, null, Now, Utc);
        Assert.Null(e.End30);
        Assert.Null(e.UsedByWritesPercent);
    }
}

public class DailyCadenceTests
{
    private static readonly DateOnly D1 = new(2026, 10, 1);

    private static CounterPoint P(int day, long value, long poh) =>
        new(new DateTimeOffset(D1.AddDays(day - 1).ToDateTime(new TimeOnly(12, 37)), TimeSpan.Zero), D1.AddDays(day - 1), value, poh);

    [Fact]
    public void Daily_reads_are_the_expected_cadence_not_a_gap()
    {
        // One read a day (the RST reader), the PC on ten hours a day.
        var points = new[] { P(1, 100, 1000), P(2, 150, 1010), P(3, 220, 1020) };
        var hourly = WriteHistory.Daily(points, D1, D1.AddDays(2));
        Assert.True(hourly.Days[1].IncludesGap);   // judged as hourly, every day is "a gap"
        var daily = WriteHistory.Daily(points, D1, D1.AddDays(2), WriteHistory.DailyInterval);
        Assert.All(daily.Days, d => Assert.False(d.IncludesGap));
        Assert.Equal([0L, 50L, 70L], daily.Days.Select(d => d.Units!.Value));
    }

    [Fact]
    public void A_missed_daily_read_still_marks_the_day_after()
    {
        var daily = WriteHistory.Daily([P(1, 100, 1000), P(4, 400, 1030)], D1, D1.AddDays(3), WriteHistory.DailyInterval);
        Assert.Equal([DayState.Known, DayState.Unknown, DayState.Unknown, DayState.Known], daily.Days.Select(d => d.State));
        Assert.True(daily.Days[3].IncludesGap);
    }
}
