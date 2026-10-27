using NandWatch.Core.Data;
using NandWatch.Core.Query;
using NandWatch.Core.View;

namespace NandWatch.Core.Tests;

public class SeriesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_gap_longer_than_allowed_breaks_the_line()
    {
        TimePoint P(double hours) => new(T0.AddHours(hours), hours);
        var runs = Series.Runs([P(0), P(1), P(2), P(6), P(7)], TimeSpan.FromHours(2));
        Assert.Equal([3, 2], runs.Select(r => r.Count));
    }

    [Fact]
    public void Last_per_day_keeps_each_days_final_item_in_order()
    {
        var items = new[] { (D: 1, V: 10), (D: 1, V: 11), (D: 2, V: 20), (D: 4, V: 40), (D: 4, V: 41) };
        var last = Series.LastPerDay(items, x => new DateOnly(2026, 10, x.D));
        Assert.Equal([11, 20, 41], last.Select(x => x.V));
    }

    [Fact]
    public void Temperatures_and_free_space_come_back_from_the_history()
    {
        using var db = new TestDb();
        var drive = new DriveIdentity("Example", "0000_0000_0000_0009.", "FW1");
        var id = HealthDb.UpsertDrive(db.Conn, drive, T0);
        for (var h = 0; h < 30; h++)
        {
            var at = T0.AddHours(h);
            var run = HealthDb.StartRun(db.Conn, "scheduled", at);
            HealthDb.InsertReading(db.Conn, run, id, new DriveReading { Drive = drive, DiskNumber = 0, At = at, SensorTemperatureC = (short)(30 + h % 5) }, TimeZoneInfo.Utc);
            HealthDb.InsertVolume(db.Conn, run, id, new VolumeReading("C:", 0, 1_000, 500 - h), at, TimeZoneInfo.Utc);
            HealthDb.FinishRun(db.Conn, run, "ok", 1, null, 10, null, at);
        }

        var temps = HealthQueries.Temperatures(db.Conn, id, T0.AddHours(20));
        Assert.Equal(10, temps.Count);
        Assert.Equal(30 + 20 % 5, temps[0].Value);

        var free = Assert.Single(HealthQueries.FreeSpace(db.Conn, id, T0));
        Assert.Equal("C:", free.Letter);
        Assert.Equal(1_000, free.TotalBytes);
        // Two local days, each its last reading: hour 23, then hour 29.
        Assert.Equal([500d - 23, 500d - 29], free.Free.Select(p => p.Value));
        Assert.Equal(30, HealthQueries.Summary(db.Conn).Runs);
    }
}

public class HeatmapTests
{
    private static readonly DateOnly Sat = new(2026, 10, 24);   // a Saturday: row 0

    [Fact]
    public void Each_kind_of_day_keeps_its_own_cell()
    {
        var from = Sat.AddDays(1);   // the first reading on Sunday
        var today = Sat.AddDays(5);
        var days = new List<DayWrites>
        {
            new(from, 2, DayState.Known, false),
            new(from.AddDays(1), 0, DayState.Off, false),
            new(from.AddDays(2), null, DayState.Unknown, false),
            new(from.AddDays(3), 0, DayState.Known, false),
            new(from.AddDays(4), 5, DayState.Known, true),
        };
        var block = Assert.Single(Heatmap.Blocks(days, from, today));
        var week = block.Cells.Where(c => c.Column == 0).ToList();
        Assert.Equal(
            [CellKind.Hidden, CellKind.Known, CellKind.Off, CellKind.Unknown, CellKind.Known, CellKind.Known, CellKind.Hidden],
            week.Select(c => c.Kind));
        Assert.Equal(2 * 512_000L, week[1].Bytes);
        Assert.True(week[5].IncludesGap);
        Assert.Equal(7 * 512_000L, block.Total);
        Assert.Equal(5 * 512_000L, block.Peak);
        Assert.Equal(2, block.ActiveDays);   // a known quiet day is not active
        Assert.All(block.Cells.Where(c => c.Column > 0), c => Assert.Equal(CellKind.Hidden, c.Kind));
        Assert.Equal(from, block.First);
    }

    [Fact]
    public void A_long_history_is_split_into_contiguous_26_week_blocks()
    {
        var blocks = Heatmap.Blocks([], Sat, Sat.AddDays(26 * 7));
        Assert.Equal(2, blocks.Count);
        Assert.Equal(blocks[0].Last.AddDays(1), blocks[1].First);
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(5, 100, 1)]
    [InlineData(30, 100, 3)]
    [InlineData(100, 100, 5)]
    public void Steps_follow_the_ramp(long value, long max, int step) => Assert.Equal(step, Heatmap.Step(value, max));
}

public class TimeTickTests
{
    private static readonly TimeZoneInfo Dhaka = TimeZoneInfo.CreateCustomTimeZone("UTC+6", TimeSpan.FromHours(6), "UTC+6", "UTC+6");

    [Fact]
    public void Two_days_get_hour_ticks_on_round_local_hours()
    {
        var start = new DateTimeOffset(2026, 10, 24, 14, 20, 0, TimeSpan.FromHours(6));
        var ticks = TimeTicks.For(start, start.AddDays(2), Dhaka, 8);
        Assert.InRange(ticks.Count, 2, 8);
        Assert.All(ticks, t => Assert.Equal(0, TimeZoneInfo.ConvertTime(t.At, Dhaka).Minute));
        Assert.Contains(ticks, t => t.Label == "Oct 25");   // midnight names its day
    }

    [Fact]
    public void Years_ahead_get_year_ticks()
    {
        var start = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.FromHours(6));
        var ticks = TimeTicks.For(start, start.AddYears(6), Dhaka, 7);
        Assert.Equal(["2027", "2028", "2029", "2030", "2031", "2032"], ticks.Select(t => t.Label));
    }

    [Fact]
    public void Months_name_the_year_on_january_and_the_first_tick()
    {
        var start = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.FromHours(6));
        var ticks = TimeTicks.For(start, start.AddMonths(5), Dhaka, 6);
        Assert.Equal(["Oct 2026", "Nov", "Dec", "Jan 2027", "Feb"], ticks.Select(t => t.Label));
    }
}
