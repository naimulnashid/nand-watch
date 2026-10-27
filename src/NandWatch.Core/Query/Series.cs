namespace NandWatch.Core.Query;

/// <summary>A value at a moment: one point of a chart's line.</summary>
public sealed record TimePoint(DateTimeOffset At, double Value);

/// <summary>One volume's free space over time, and its size as last seen.</summary>
public sealed record VolumeSeries(string Letter, long TotalBytes, IReadOnlyList<TimePoint> Free);

/// <summary>Shaping readings into what a chart draws.</summary>
public static class Series
{
    /// <summary>
    /// Runs of points no more than <paramref name="maxGap"/> apart. A longer
    /// gap is a break in the line: the collector was not running, and a line
    /// sloping across it would invent readings.
    /// </summary>
    public static List<List<TimePoint>> Runs(IReadOnlyList<TimePoint> points, TimeSpan maxGap)
    {
        var runs = new List<List<TimePoint>>();
        List<TimePoint>? current = null;
        TimePoint? previous = null;
        foreach (var p in points.OrderBy(p => p.At))
        {
            if (current is null || previous is null || p.At - previous.At > maxGap) runs.Add(current = []);
            current.Add(p);
            previous = p;
        }
        return runs;
    }

    /// <summary>
    /// The last item of each local day, in order. An hourly reading is finer
    /// than a pixel on a chart of months; the day's last one carries the
    /// day's whole change for a cumulative counter.
    /// </summary>
    public static List<T> LastPerDay<T>(IEnumerable<T> sorted, Func<T, DateOnly> day)
    {
        var result = new List<T>();
        DateOnly? current = null;
        foreach (var item in sorted)
        {
            var d = day(item);
            if (current == d) result[^1] = item;
            else
            {
                result.Add(item);
                current = d;
            }
        }
        return result;
    }
}
