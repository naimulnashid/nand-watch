using NandWatch.Core.Nvme;
using NandWatch.Core.Query;

namespace NandWatch.Core.View;

/// <summary>What one day of the heat map is. Each kind is drawn differently, and conflating two invents or hides history.</summary>
public enum CellKind
{
    /// <summary>After today, or before the drive's first reading: not drawn.</summary>
    Hidden,

    /// <summary>The drive ran and nothing collected: outlined, never a quiet day.</summary>
    Unknown,

    /// <summary>The drive was off (its power-on hours prove it): a true zero.</summary>
    Off,

    /// <summary>Read that day, quiet or not.</summary>
    Known,
}

public sealed record HeatmapCell(DateOnly Date, long Bytes, CellKind Kind, bool IncludesGap, int Column, int Row);

public sealed record HeatmapBlock(
    IReadOnlyList<HeatmapCell> Cells,
    IReadOnlyList<(string Label, int Column)> Months,
    DateOnly First,
    DateOnly Last,
    long Peak,
    long Total,
    int ActiveDays);

/// <summary>
/// The calendar arithmetic behind the Activity heat map of bytes written a day.
/// </summary>
/// <remarks>
/// Data Usage Native's layout: contiguous 26-week blocks (half-years would
/// need 27 or 28 columns and split a week at every seam), weeks Saturday to
/// Friday, the first block opening on the week of the first reading.
/// </remarks>
public static class Heatmap
{
    public const int Weeks = 26;

    public static readonly string[] DayLabels = ["Sat", "Sun", "Mon", "Tue", "Wed", "Thu", "Fri"];

    private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>Saturday-first: Saturday is row 0.</summary>
    public static int RowOf(DateOnly d) => ((int)d.DayOfWeek + 1) % 7;

    private static DateOnly WeekStartOf(DateOnly d) => d.AddDays(-RowOf(d));

    /// <summary>
    /// Every block from <paramref name="from"/> (the first reading's day) to
    /// <paramref name="today"/>, oldest first. Days outside the series are
    /// unknown; days before <paramref name="from"/> or after today are hidden.
    /// </summary>
    public static List<HeatmapBlock> Blocks(IReadOnlyList<DayWrites> days, DateOnly from, DateOnly today)
    {
        var byDate = days.ToDictionary(d => d.Date);
        var blocks = new List<HeatmapBlock>();
        for (var week = WeekStartOf(from); week <= today; week = week.AddDays(Weeks * 7))
            blocks.Add(Build(byDate, week, from, today));
        return blocks;
    }

    private static HeatmapBlock Build(Dictionary<DateOnly, DayWrites> byDate, DateOnly firstWeek, DateOnly from, DateOnly today)
    {
        var cells = new List<HeatmapCell>(Weeks * 7);
        var months = new List<(string, int)>();
        long peak = 0, total = 0;
        int active = 0, lastMonth = -1;

        for (var column = 0; column < Weeks; column++)
        {
            var weekStart = firstWeek.AddDays(column * 7);
            // A column is labelled by the first day it actually draws.
            var labelDay = weekStart < from ? from : weekStart;
            if (labelDay <= today && labelDay.Month != lastMonth)
            {
                months.Add((MonthNames[labelDay.Month - 1], column));
                lastMonth = labelDay.Month;
            }

            for (var row = 0; row < 7; row++)
            {
                var day = weekStart.AddDays(row);
                if (day > today || day < from)
                {
                    cells.Add(new HeatmapCell(day, 0, CellKind.Hidden, false, column, row));
                    continue;
                }
                var d = byDate.GetValueOrDefault(day);
                var kind = d?.State switch
                {
                    DayState.Known => CellKind.Known,
                    DayState.Off => CellKind.Off,
                    _ => CellKind.Unknown,
                };
                var bytes = (d?.Units ?? 0) * HealthLog.BytesPerDataUnit;
                if (kind == CellKind.Known)
                {
                    peak = Math.Max(peak, bytes);
                    total += bytes;
                    if (bytes > 0) active++;
                }
                cells.Add(new HeatmapCell(day, bytes, kind, d?.IncludesGap ?? false, column, row));
            }
        }

        // A block opening on a month's last week labels that month and the
        // next side by side, and "OctNov" overlap. Drop the stub.
        if (months.Count > 1 && months[1].Item2 - months[0].Item2 < 3) months.RemoveAt(0);

        var start = firstWeek < from ? from : firstWeek;
        return new HeatmapBlock(cells, months, start, firstWeek.AddDays(Weeks * 7 - 1), peak, total, active);
    }

    /// <summary>
    /// The ramp step, 0 to 5, for a day's bytes against the peak. Data Usage
    /// Native's thresholds: a small day still shows, the peak alone is 5.
    /// </summary>
    public static int Step(long value, long max)
    {
        if (value <= 0 || max <= 0) return 0;
        var ratio = (double)value / max;
        return ratio <= 0.05 ? 1 : ratio <= 0.15 ? 2 : ratio <= 0.35 ? 3 : ratio <= 0.7 ? 4 : 5;
    }

    /// <summary>"Oct 18, 2026 – Apr 16, 2027"-style, with one year when both ends share it.</summary>
    public static string BlockLabel(HeatmapBlock block)
    {
        static string Short(DateOnly d) => $"{MonthNames[d.Month - 1]} {d.Day}";
        return block.First.Year == block.Last.Year
            ? $"{Short(block.First)} – {Short(block.Last)}, {block.Last.Year}"
            : $"{Short(block.First)}, {block.First.Year} – {Short(block.Last)}, {block.Last.Year}";
    }
}
