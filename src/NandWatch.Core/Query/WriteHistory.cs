namespace NandWatch.Core.Query;

/// <summary>One reading of a cumulative counter (data units written, say) for one drive.</summary>
public sealed record CounterPoint(DateTimeOffset At, DateOnly LocalDate, long Value, long? PowerOnHours);

public enum DayState
{
    /// <summary>The collector read the drive that day.</summary>
    Known,

    /// <summary>No reading, but the drive's power-on hours prove it was off: a true zero.</summary>
    Off,

    /// <summary>No reading and no proof: drawn as a break, never as zero.</summary>
    Unknown,
}

/// <summary>
/// A day's data units. <see cref="IncludesGap"/> marks a day whose total
/// includes writes from a stretch the drive RAN without being read (or one
/// that cannot be told apart from it): they happened earlier, but are filed
/// here. A night the drive spent off sets no mark: it wrote nothing.
/// </summary>
public sealed record DayWrites(DateOnly Date, long? Units, DayState State, bool IncludesGap);

/// <summary>A counter that went backwards on the same drive: a new baseline, never negative use.</summary>
public sealed record CounterReset(DateTimeOffset At, long From, long To);

public sealed record WriteSeries(IReadOnlyList<DayWrites> Days, IReadOnlyList<CounterReset> Resets);

/// <summary>
/// Turns cumulative readings into daily totals, keeping zero and unknown apart.
/// </summary>
/// <remarks>
/// <para><b>A delta is filed under the day of the reading that observed it.</b>
/// Readings an hour apart misplace at most an hour of writes across midnight.</para>
/// <para><b>A gap is resolved by the drive's own clock.</b> Between readings more
/// than <see cref="ShortInterval"/> apart, the days strictly inside are
/// <see cref="DayState.Off"/> when power-on hours grew by at most
/// <see cref="OffTolerance"/> (the laptop was shut), else
/// <see cref="DayState.Unknown"/> (it ran, but nothing collected).</para>
/// </remarks>
public static class WriteHistory
{
    public static readonly TimeSpan ShortInterval = TimeSpan.FromHours(2);

    /// <summary>
    /// The gap allowed between readings of a drive read once a day (its health
    /// log through the RST reader): a day and a bit, so a read that ran a few
    /// hours late is still the expected cadence, not an unrecorded stretch.
    /// </summary>
    public static readonly TimeSpan DailyInterval = TimeSpan.FromHours(30);

    /// <summary>Power-on hours are whole hours, so one can tick over across a shutdown.</summary>
    public const long OffTolerance = 1;

    /// <param name="interval">
    /// The drive's expected gap between readings: <see cref="ShortInterval"/>
    /// (hourly, the default) or <see cref="DailyInterval"/> (once a day, where
    /// each day's total is the writes since the read before). Only a longer gap
    /// is resolved as off or unknown and marks the day after it.
    /// </param>
    public static WriteSeries Daily(IReadOnlyList<CounterPoint> points, DateOnly from, DateOnly to, TimeSpan? interval = null)
    {
        var expected = interval ?? ShortInterval;
        var sorted = points.OrderBy(p => p.At).ToList();
        var sums = new Dictionary<DateOnly, long>();
        var gapEnds = new HashSet<DateOnly>();
        var off = new HashSet<DateOnly>();
        var resets = new List<CounterReset>();
        foreach (var p in sorted) sums.TryAdd(p.LocalDate, 0);

        for (var i = 1; i < sorted.Count; i++)
        {
            var (prev, cur) = (sorted[i - 1], sorted[i]);
            var delta = cur.Value - prev.Value;
            if (delta < 0)
            {
                resets.Add(new CounterReset(cur.At, prev.Value, cur.Value));
                delta = 0;
            }
            sums[cur.LocalDate] += delta;
            if (cur.At - prev.At <= expected) continue;

            var wasOff = prev.PowerOnHours is { } a && cur.PowerOnHours is { } b && b - a <= OffTolerance;
            if (wasOff)
                for (var d = prev.LocalDate.AddDays(1); d < cur.LocalDate; d = d.AddDays(1)) off.Add(d);
            else gapEnds.Add(cur.LocalDate);
        }

        var days = new List<DayWrites>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (sums.TryGetValue(d, out var units)) days.Add(new DayWrites(d, units, DayState.Known, gapEnds.Contains(d)));
            else if (off.Contains(d)) days.Add(new DayWrites(d, 0, DayState.Off, false));
            else days.Add(new DayWrites(d, null, DayState.Unknown, false));
        }
        return new WriteSeries(days, resets);
    }
}
