using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NandWatch.App.Charts;
using NandWatch.App.Controls;
using NandWatch.App.Theme;
using NandWatch.Core.Data;
using NandWatch.Core.Nvme;
using NandWatch.Core.Query;
using NandWatch.Core.View;
using NandWatch.Windows.Collect;
using Projection = NandWatch.Core.Query.Projection;

namespace NandWatch.App.Views;

/// <summary>Everything one drive page draws, read in one go off the UI thread.</summary>
public sealed record DriveDetail(
    DriveSummary Summary,
    IReadOnlyList<TimePoint> Written,
    IReadOnlyList<DayWrites> Writes,
    IReadOnlyList<DayWrites> Reads,
    IReadOnlyList<TimePoint> Temperatures,
    IReadOnlyList<VolumeSeries> FreeSpace,
    IReadOnlyList<DayWrites> AllWrites);

/// <summary>
/// One drive over time: how far through its rating it is and when it gets
/// there, what it writes a day, its temperature and its free space. Each
/// chart has its own range, kept while the page is open.
/// </summary>
public sealed class DrivePage(PageContext ctx, long driveId) : IPage
{
    /// <summary>The projection's axis stops this far ahead; a slower pace is said in words.</summary>
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(15 * 365.25);

    private int _writeDays = 30, _tempDays = 30, _spaceDays = 30;   // 0 is all of the history
    private DriveDetail? _d;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private DateTimeOffset? _first;

    public void Load()
    {
        _now = DateTimeOffset.UtcNow;
        var zone = TimeZoneInfo.Local;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_now, zone).DateTime);
        using var conn = HealthDb.OpenRead(ctx.State.DatabasePath!);
        var drive = HealthQueries.Drives(conn).FirstOrDefault(x => x.Id == driveId)
            ?? throw new InvalidOperationException("That drive is not in the history.");
        var latest = HealthQueries.Latest(conn, drive.Id);
        var written = HealthQueries.Written(conn, drive.Id);
        var read = HealthQueries.Read(conn, drive.Id);
        var life = latest is { HasHealth: true } ? Projection.Estimate(written, latest.PercentageUsed, drive.RatedTbw, _now, zone) : null;
        var refused = HealthQueries.Refusals(conn).FirstOrDefault(r => r.Model == drive.Model && r.Request == Collector.HealthRequest);
        var historyDays = written.Count == 0 ? 0 : (int)(written[^1].At - written[0].At).TotalDays;
        var summary = new DriveSummary(drive, latest, life, HealthQueries.LatestVolumes(conn, drive.Id), HealthQueries.ErrorLogSince(conn, drive.Id), refused, historyDays);

        // The range starts no earlier than the first reading: days before it
        // are not "unknown", they are before this app existed.
        var first = written.Count > 0 ? written[0].LocalDate : today;
        var from = _writeDays == 0 || first > today.AddDays(1 - _writeDays) ? first : today.AddDays(1 - _writeDays);
        var since = (int days) => days == 0 ? DateTimeOffset.MinValue : _now.AddDays(-days);
        _first = HealthQueries.FirstReading(conn, drive.Id);

        _d = new DriveDetail(
            summary,
            // The cumulative line is one point a day. It is drawn unbroken: both
            // ends of any gap are measured, and the counter only climbs.
            Series.LastPerDay(written, p => p.LocalDate).Select(p => new TimePoint(p.At, (double)HealthLog.Terabytes((UInt128)p.Value))).ToList(),
            WriteHistory.Daily(written, from, today, HealthQueries.HealthInterval(conn, drive.Id)).Days,
            WriteHistory.Daily(read, from, today, HealthQueries.HealthInterval(conn, drive.Id)).Days,
            HealthQueries.Temperatures(conn, drive.Id, since(_tempDays)),
            HealthQueries.FreeSpace(conn, drive.Id, since(_spaceDays)),
            // The heat map is the whole history, whatever the charts above show.
            written.Count == 0 ? [] : WriteHistory.Daily(written, first, today, HealthQueries.HealthInterval(conn, drive.Id)).Days);
    }

    public void Placeholder()
    {
        _now = DateTimeOffset.UtcNow;
        _first = null;
        _d = Views.Placeholder.Drive(_now);
    }

    public UIElement Build()
    {
        var d = _d!;
        var s = d.Summary;
        var page = new StackPanel();
        var back = Ui.Link("← Overview", () => ctx.Navigate(new Route(PageKind.Overview)));
        back.Margin = new Thickness(0, 0, 0, 10);
        var letters = string.Join(" ", s.Volumes.Select(v => v.Letter));
        var model = s.Drive.Title == s.Drive.Model.Trim() ? "" : s.Drive.Model.Trim();
        var sub = string.Join(" · ", new[] { letters, model, $"firmware {s.Drive.Firmware}" }.Where(x => x.Length > 0));
        page.Children.Add(Parts.PageHead(s.Drive.Title, sub, before: back, after: OverviewPage.Status(s)));

        if (s.Latest is { HasHealth: true } && s.Life is { } life)
        {
            page.Children.Add(LifePanel(d, life));
            page.Children.Add(WritesPanel(d));
        }
        else
            page.Children.Add(Ui.Panel("Writes and wear", null, null, Parts.Note(s.HealthRefused is not null
                ? "This drive's health log is not readable through Intel RST from a standard account, so there is no history of its writes or wear yet. Its temperature and free space are below."
                : "No health log has been read from this drive yet, so there is no history of its writes or wear.")));
        page.Children.Add(TemperaturePanel(d));
        page.Children.Add(FreeSpacePanel(d));
        if (d.AllWrites.Count > 0) page.Children.Add(ActivityPanel(d.AllWrites));
        return page;
    }

    /* ------------------------------------------------------------ Activity */

    /// <summary>
    /// Bytes written a day as a calendar, Data Usage Native's layout: every
    /// day since the first reading, in 26-week blocks stacked oldest first,
    /// one colour scale for all of them. Outlined days had no reading, which
    /// is not the same as a quiet day.
    /// </summary>
    private static Border ActivityPanel(IReadOnlyList<DayWrites> days)
    {
        var from = days[0].Date;
        var to = days[^1].Date;
        var blocks = Heatmap.Blocks(days, from, to);
        var max = blocks.Count == 0 ? 0 : blocks.Max(b => b.Peak);
        var body = new StackPanel();
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var head = new Grid { Margin = new Thickness(7, i == 0 ? 0 : 22, 7, 4) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Ui.Text(Heatmap.BlockLabel(block), 15, 600));
            var total = Ui.Text(Format.BytesText(block.Total), 15, 500, Palette.TextMutedBrush, numeric: true);
            Grid.SetColumn(total, 1);
            head.Children.Add(total);
            body.Children.Add(head);
            body.Children.Add(new HeatmapView(block, max));
        }
        body.Children.Add(HeatmapView.Legend(blocks.Sum(b => b.Total), blocks.Sum(b => b.ActiveDays), $"since {Format.Date(from)}"));
        return Ui.Panel("Activity", "Data written each day, in blocks of 26 weeks. Outlined days had no reading.", null, body);
    }

    /* ---------------------------------------------------------------- Life */

    private Border LifePanel(DriveDetail d, LifeEstimate life)
    {
        var body = new StackPanel { Spacing = 14 };
        var rated = (double?)life.RatedTbw;
        var written = (double)life.WrittenTb;
        var start = d.Written.Count > 0 ? d.Written[0].At : _now;
        var last = d.Written.Count > 0 ? d.Written[^1] : new TimePoint(_now, written);

        // Each pace is drawn from the newest reading to where it meets the
        // rating, cut at the horizon when it meets it later than that.
        var horizon = _now + Horizon;
        var projections = new List<Projected>();
        var end = _now;
        void Pace(DateOnly? at, Microsoft.UI.Xaml.Media.Brush brush)
        {
            if (at is not { } date || rated is not { } r) return;
            var when = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(date.ToDateTime(TimeOnly.MinValue)));
            var to = new TimePoint(when, r);
            if (when > horizon)
                to = new TimePoint(horizon, last.Value + (r - last.Value) * (horizon - last.At).TotalSeconds / Math.Max(1, (when - last.At).TotalSeconds));
            projections.Add(new Projected(last, to, brush));
            if (to.At > end) end = to.At;
        }
        Pace(life.End30, Palette.AccentBrightBrush);
        Pace(life.End90, Palette.TextMutedBrush);
        // No projection: the axis ends at now, with a range to draw even on a first reading.
        if (end - start < TimeSpan.FromHours(1)) end = start.AddHours(1);

        var chart = new TimeLineChart(
            [new LineSeries("Written", Palette.Accent, [d.Written.ToList()], Fill: true)],
            start, end, Math.Max(rated ?? 0, written),
            Format.TbTick, v => $"{v:N2} TB written",
            rated is { } rr ? [new Reference(rr, $"{Format.Tbw(life.RatedTbw!.Value)} rating", Palette.TextMutedBrush)] : null,
            projections, daily: true);
        body.Children.Add(chart);
        body.Children.Add(Parts.Note(LifeNote(life)));
        var subtitle = rated is null
            ? "Data written over the drive's life, from its own counter. No endurance rating is known for this model."
            : "Data written over the drive's life, from its own counter, against its endurance rating. Dashed: where the last 30 days' pace (bright) and 90 days' pace (dim) reach the rating.";
        return Ui.Panel("Life", subtitle, null, body);
    }

    private static string LifeNote(LifeEstimate life)
    {
        var used = life.UsedByWritesPercent is { } u ? $"{Format.BytesText((double)life.WrittenTb * 1e12)} of {Format.Tbw(life.RatedTbw!.Value)} written ({u:N1}%)." : $"{Format.BytesText((double)life.WrittenTb * 1e12)} written.";
        if (life.Rate30 is not { } r30)
            return $"{used} The projection needs {Projection.MinimumHistory.TotalDays:0} days of history.";
        string At(WriteRate? rate, DateOnly? end, string window) => rate is null ? ""
            : end is { } e ? $" At the last {window}' pace ({Format.BytesText((double)rate.TbPerDay * 1e12)} a day) the rating is reached {Format.Date(e)}."
            : $" At the last {window}' pace ({Format.BytesText((double)rate.TbPerDay * 1e12)} a day) it is more than a century away.";
        var paces = life.RatedTbw is null ? $" The last 30 days' pace is {Format.BytesText((double)r30.TbPerDay * 1e12)} a day."
            : At(life.Rate30, life.End30, "30 days") + At(life.Rate90, life.End90, "90 days");
        return $"{used}{paces} TBW is the warranty's limit, not a failure date.";
    }

    /* -------------------------------------------------------------- Writes */

    private Border WritesPanel(DriveDetail d)
    {
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(new DailyWritesChart(d.Writes, d.Reads));
        var measured = d.Writes.Where(x => x.State != DayState.Unknown).ToList();
        if (measured.Count > 0)
        {
            var bytes = measured.Sum(x => (double)(x.Units ?? 0) * HealthLog.BytesPerDataUnit);
            var busiest = measured.MaxBy(x => x.Units ?? 0)!;
            var unknown = d.Writes.Count - measured.Count;
            body.Children.Add(Parts.Note(
                $"{Format.BytesText(bytes)} over {measured.Count} {(measured.Count == 1 ? "day" : "days")} with a reading, {Format.BytesText(bytes / measured.Count)} a day on average. " +
                $"Busiest: {Format.DayLong(busiest.Date)}, {Format.BytesText((double)(busiest.Units ?? 0) * HealthLog.BytesPerDataUnit)}." +
                (unknown > 0 ? $" {unknown} {(unknown == 1 ? "day has" : "days have")} no reading (outlined)." : "")));
        }
        var subtitle = d.Summary.Latest?.HealthSource == Core.Data.DriveReading.RstSource
            ? "From the drive's own counter, read once a day through Intel RST: each bar holds the writes since the read before, filed under the day of the read. Paler: a read was missed, and the total covers more than a day."
            : "From the drive's own counter, filed under the day of the reading that saw it. Paler: the total includes writes from an unrecorded stretch before it.";
        return Ui.Panel("Written per day", subtitle,
            Range("Written per day range", [30, 90, 0], _writeDays, v => _writeDays = v), body);
    }

    /* --------------------------------------------------------- Temperature */

    private Border TemperaturePanel(DriveDetail d)
    {
        var warning = d.Summary.Latest?.WarningTemperatureC;
        var aside = Range("Temperature range", [2, 7, 30], _tempDays, v => _tempDays = v);
        const string subtitle = "Windows' reading of the drive's sensor, every hour. Dashed: the temperature at which the drive warns.";
        if (d.Temperatures.Count == 0)
            return Ui.Panel("Temperature", subtitle, aside, Parts.Note("No temperature readings in this range."));
        var body = new StackPanel { Spacing = 14 };
        var max = d.Temperatures.Max(p => p.Value);
        body.Children.Add(new TimeLineChart(
            [new LineSeries("Temperature", Palette.Accent, Series.Runs(d.Temperatures, WriteHistory.ShortInterval))],
            HealthQueries.WindowStart(_now, _tempDays, _first), _now, Math.Max(max, warning ?? 0),
            v => $"{v:0} °C", v => $"{v:0} °C",
            warning is { } w ? [new Reference(w, $"Warns at {w} °C", Palette.CautionBrush)] : null));
        body.Children.Add(Parts.Note($"Highest {max:0} °C, lowest {d.Temperatures.Min(p => p.Value):0} °C, over {d.Temperatures.Count} readings."));
        return Ui.Panel("Temperature", subtitle, aside, body);
    }

    /* ---------------------------------------------------------- Free space */

    private Border FreeSpacePanel(DriveDetail d)
    {
        var aside = Range("Free space range", [30, 90, 0], _spaceDays, v => _spaceDays = v);
        const string subtitle = "Free space on each of the drive's volumes, one reading a day. A fuller drive leaves less room for its own housekeeping.";
        if (d.FreeSpace.Count == 0 || d.FreeSpace.All(v => v.Free.Count == 0))
            return Ui.Panel("Free space", subtitle, aside, Parts.Note("No free-space readings in this range."));
        var series = d.FreeSpace.Select((v, i) => new LineSeries(v.Letter, d.FreeSpace.Count == 1 ? Palette.Accent : Palette.Series(i),
            Series.Runs(v.Free, TimeSpan.FromHours(48)))).ToList();
        var start = _spaceDays == 0 ? d.FreeSpace.Min(v => v.Free[0].At) : HealthQueries.WindowStart(_now, _spaceDays, _first);
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(new TimeLineChart(series, start, _now, d.FreeSpace.Max(v => (double)v.TotalBytes),
            Axes.ByteTick, v => $"{Format.BytesText(v)} free", daily: true));
        body.Children.Add(Parts.Note(string.Join("  ·  ", d.FreeSpace.Select(v =>
            $"{v.Letter} {Format.BytesText(v.Free[^1].Value)} free of {Format.BytesText(v.TotalBytes)} ({Format.Percent(v.Free[^1].Value / v.TotalBytes * 100)})"))));
        return Ui.Panel("Free space", subtitle, aside, body);
    }

    /* --------------------------------------------------------------- Parts */

    /// <summary>A panel's range drop-down, at its top right; a choice reloads the page in place.</summary>
    private ComboBox Range(string name, int[] options, int current, Action<int> set) =>
        RangePicker.Create(name, options, current, value =>
        {
            set(value);
            ctx.Redraw();
        });
}
