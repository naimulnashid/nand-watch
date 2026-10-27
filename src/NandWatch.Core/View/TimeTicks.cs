namespace NandWatch.Core.View;

public sealed record TimeTick(DateTimeOffset At, string Label);

/// <summary>
/// Labels for a time axis that may span a day or a decade: the life
/// projection runs years ahead, a temperature curve two days back. Ticks sit
/// on round local times (midnight, the first of a month, New Year), never at
/// arbitrary offsets from the axis's start.
/// </summary>
public static class TimeTicks
{
    private enum Unit { Hour, Day, Month, Year }

    private static readonly (Unit Unit, int Step)[] Ladder =
    [
        (Unit.Hour, 1), (Unit.Hour, 2), (Unit.Hour, 3), (Unit.Hour, 6), (Unit.Hour, 12),
        (Unit.Day, 1), (Unit.Day, 2), (Unit.Day, 7), (Unit.Day, 14),
        (Unit.Month, 1), (Unit.Month, 3), (Unit.Month, 6),
        (Unit.Year, 1), (Unit.Year, 2), (Unit.Year, 5), (Unit.Year, 10), (Unit.Year, 20), (Unit.Year, 50),
    ];

    private static readonly System.Globalization.CultureInfo En = System.Globalization.CultureInfo.GetCultureInfo("en-US");

    /// <summary>The finest rung of the ladder that puts at most <paramref name="maxTicks"/> ticks between the ends.</summary>
    public static IReadOnlyList<TimeTick> For(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, int maxTicks)
    {
        if (end <= start || maxTicks < 1) return [];
        foreach (var (unit, step) in Ladder)
        {
            var ticks = Generate(start, end, zone, unit, step, maxTicks + 1);
            if (ticks.Count <= maxTicks) return ticks;
        }
        return [];
    }

    private static List<TimeTick> Generate(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, Unit unit, int step, int limit)
    {
        var localStart = TimeZoneInfo.ConvertTime(start, zone).DateTime;
        var localEnd = TimeZoneInfo.ConvertTime(end, zone).DateTime;
        var t = unit switch
        {
            Unit.Hour => new DateTime(localStart.Year, localStart.Month, localStart.Day, localStart.Hour / step * step, 0, 0),
            Unit.Day => localStart.Date,
            Unit.Month => new DateTime(localStart.Year, (localStart.Month - 1) / step * step + 1, 1),
            _ => new DateTime(localStart.Year / step * step, 1, 1),
        };
        var ticks = new List<TimeTick>();
        var first = true;
        for (; t <= localEnd && ticks.Count < limit; t = Next(t, unit, step))
        {
            if (t < localStart) continue;
            ticks.Add(new TimeTick(new DateTimeOffset(t, zone.GetUtcOffset(t)), Label(t, unit, first)));
            first = false;
        }
        return ticks;
    }

    private static DateTime Next(DateTime t, Unit unit, int step) => unit switch
    {
        Unit.Hour => t.AddHours(step),
        Unit.Day => t.AddDays(step),
        Unit.Month => t.AddMonths(step),
        _ => t.AddYears(step),
    };

    /// <summary>Hours read "14:00" (midnight names its day); days "Oct 24"; months "Oct", with the year on January and the first tick; years "2027".</summary>
    private static string Label(DateTime t, Unit unit, bool first) => unit switch
    {
        Unit.Hour => t.Hour == 0 ? t.ToString("MMM d", En) : t.ToString("HH:mm", En),
        Unit.Day => t.ToString("MMM d", En),
        Unit.Month => t.Month == 1 || first ? t.ToString("MMM yyyy", En) : t.ToString("MMM", En),
        _ => t.ToString("yyyy", En),
    };
}
