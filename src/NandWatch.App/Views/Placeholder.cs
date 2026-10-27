using NandWatch.Core.Query;

namespace NandWatch.App.Views;

/// <summary>
/// Stand-in data for the loading skeletons: the shape of a real page, no
/// database. Invented values; they are drawn as grey bars, never as figures.
/// </summary>
public static class Placeholder
{
    public static IReadOnlyList<DriveSummary> Drives(DateTimeOffset now)
    {
        DriveSummary One(long id, string model, string letter, bool health) => new(
            new DriveRow(id, model, "0000_0000_0000_0000.", "FW1.0", now, null, 150, null),
            new LatestReading(now, 40, 85, 0, health, 0, 100, 5, 20, 10_000, 1_000, 40, 0, 0, 0),
            health ? new LifeEstimate { WrittenTb = 48m, RatedTbw = 150, UsedByWritesPercent = 32m, VendorPercentUsed = 20 } : null,
            [new VolumeRow(letter, 500_000_000_000, 100_000_000_000, now)],
            null, null, 0);
        return [One(1, "Example drive", "C:", true), One(2, "Example drive", "D:", false)];
    }

    public static DriveDetail Drive(DateTimeOffset now)
    {
        var summary = Drives(now)[0];
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var days = Enumerable.Range(0, 30).Select(i => new DayWrites(today.AddDays(i - 29), 100_000 + i % 5 * 20_000, DayState.Known, false)).ToList();
        var hours = Enumerable.Range(0, 7 * 24).Select(i => new TimePoint(now.AddHours(i - 7 * 24), 40 + i % 6)).ToList();
        var space = Enumerable.Range(0, 90).Select(i => new TimePoint(now.AddDays(i - 90), 100e9)).ToList();
        return new DriveDetail(summary,
            Enumerable.Range(0, 30).Select(i => new TimePoint(now.AddDays(i - 30), 40 + i * 0.1)).ToList(),
            days, days, hours, [new VolumeSeries("C:", 500_000_000_000, space)], days);
    }

    public static SyncData Sync(DateTimeOffset now) => new(
        new HistorySummary(1000, 500, now.AddDays(-30), now, now),
        Enumerable.Range(0, 8).Select(i => new RunRow(i, now.AddHours(-i), "scheduled", "ok", 2, null, 100, null)).ToList(),
        [],
        @"D:\Example\nand-watch");
}
