using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NandWatch.App.Charts;
using NandWatch.App.Theme;
using NandWatch.Core.Data;
using NandWatch.Core.Query;
using NandWatch.Core.View;
using NandWatch.Windows.Collect;
using Projection = NandWatch.Core.Query.Projection;

namespace NandWatch.App.Views;

/// <summary>One drive as the overview draws it.</summary>
public sealed record DriveSummary(
    DriveRow Drive,
    LatestReading? Latest,
    LifeEstimate? Life,
    IReadOnlyList<VolumeRow> Volumes,
    (DateTimeOffset Since, long Value)? ErrorLog,
    RefusalRow? HealthRefused,
    int HistoryDays);

/// <summary>
/// Every drive: its life used, its health, its pace and its free space. The
/// two life figures (written against the rating, and the drive's own) stand
/// side by side and are never blended.
/// </summary>
public sealed class OverviewPage(PageContext ctx) : IPage
{
    private IReadOnlyList<DriveSummary> _drives = [];
    private DateTimeOffset? _lastReading;
    private bool _rstInstalled;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public void Load()
    {
        _now = DateTimeOffset.UtcNow;
        using var conn = HealthDb.OpenRead(ctx.State.DatabasePath!);
        _drives = Summaries(conn, _now);
        _rstInstalled = CollectorTask.Query(CollectorTask.RstReaderName) is { Exists: true };
        _lastReading = _drives.Select(d => d.Latest?.At).Max();
    }

    /// <summary>Every drive as the Overview (and the tray's tooltip) sees it, in display order.</summary>
    public static IReadOnlyList<DriveSummary> Summaries(Microsoft.Data.Sqlite.SqliteConnection conn, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.Local;
        var refusals = HealthQueries.Refusals(conn);
        var list = new List<DriveSummary>();
        foreach (var drive in HealthQueries.Drives(conn))
        {
            var latest = HealthQueries.Latest(conn, drive.Id);
            var written = HealthQueries.Written(conn, drive.Id);
            var life = latest is { HasHealth: true } ? Projection.Estimate(written, latest.PercentageUsed, drive.RatedTbw, now, zone) : null;
            var days = written.Count == 0 ? 0 : (int)(written[^1].At - written[0].At).TotalDays;
            list.Add(new DriveSummary(drive, latest, life, HealthQueries.LatestVolumes(conn, drive.Id), HealthQueries.ErrorLogSince(conn, drive.Id),
                refusals.FirstOrDefault(r => r.Model == drive.Model && r.Request == Collector.HealthRequest), days));
        }
        return Order(list);
    }

    public void Placeholder()
    {
        _now = DateTimeOffset.UtcNow;
        _drives = Views.Placeholder.Drives(_now);
        _lastReading = _now;
    }

    public UIElement Build()
    {
        var page = new StackPanel();
        var sub = _lastReading is { } last
            ? $"Last reading {Format.When(last, _now, TimeZoneInfo.Local)} · the next at {NextRun(_now):HH:mm}"
            : null;
        page.Children.Add(Parts.PageHead("Drives", sub));
        var delay = 0;
        foreach (var d in _drives) page.Children.Add(DrivePanel(d, delay += 60));
        if (_drives.Any(d => d.Volumes.Count > 0)) page.Children.Add(FreeSpacePanel(_drives, delay + 60));
        page.Children.Add(Parts.Note("Terabytes here are decimal (10^12 bytes), the unit endurance ratings use. HWiNFO and similar tools show binary TiB as \"TBytes\", about 10% lower for the same bytes."));
        return page;
    }

    /// <summary>
    /// Drives needing attention first (red, then amber), then by drive letter,
    /// so the drive at risk is never below the fold behind a healthy one.
    /// Windows' disk numbers decide nothing.
    /// </summary>
    public static IReadOnlyList<DriveSummary> Order(IEnumerable<DriveSummary> drives) => drives
        .OrderBy(d => Severity(d) switch { Ui.BadgeKind.Bad => 0, Ui.BadgeKind.Caution => 1, _ => 2 })
        .ThenBy(d => d.Volumes.Count > 0 ? d.Volumes.Min(v => v.Letter) : "~")
        .ToList();

    /// <summary>
    /// Read now: the RST reader's task, started without a prompt, then a
    /// collection to fold its page in. The page refreshes when it lands.
    /// </summary>
    public static HyperlinkButton ReadNowLink(PageContext ctx)
    {
        HyperlinkButton? link = null;
        link = Ui.Link("Read health log now", async () =>
        {
            var label = (TextBlock)link!.Content;
            link.IsEnabled = false;
            label.Text = "Reading through Intel RST…";
            var why = await ctx.State.ReadThroughRstAsync();
            label.Text = "Read health log now";
            link.IsEnabled = true;
            if (why is not null) await Shell.ShowMessage(ctx.Window, "The RST reader did not read the drive", why);
        });
        return link;
    }

    /// <summary>The scheduled task's next hourly run, at seven past.</summary>
    public static DateTime NextRun(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).DateTime;
        var next = local.Date.AddHours(local.Hour).AddMinutes(7);
        return next > local ? next : next.AddHours(1);
    }

    private Border DrivePanel(DriveSummary d, int delay)
    {
        var letters = string.Join(" ", d.Volumes.Select(v => v.Letter));
        // The model as Windows reports it, when the title is a readable name or a label.
        var model = d.Drive.Title == d.Drive.Model.Trim() ? "" : d.Drive.Model.Trim();
        // A health log from the RST reader is daily: say so, and how old it is.
        var source = d.Latest is { HealthSource: "rst", HealthAt: { } at } ? $"health log through Intel RST, {Format.When(at, _now, TimeZoneInfo.Local)}" : "";
        var subtitle = string.Join(" · ", new[] { letters, model, $"firmware {d.Drive.Firmware}", source }.Where(s => s.Length > 0));
        var body = new StackPanel { Spacing = 18.4 };
        var latest = d.Latest;

        if (latest is { HasHealth: true } && d.Life is { } life)
        {
            var used = life.UsedByWritesPercent;
            body.Children.Add(Parts.Grid(220,
                Parts.FigureCard("Life used by writes",
                    used is { } u ? Parts.NumberValue((double)u, v => v.ToString("N1"), "%", Palette.AccentBrightBrush) : Parts.TextValue("No rating"),
                    used is null ? $"{Format.BytesText((double)life.WrittenTb * 1e12)} written; no endurance rating is known for this model"
                                 : $"{Format.BytesText((double)life.WrittenTb * 1e12)} of {Format.Tbw(life.RatedTbw!.Value)} written", delay),
                Parts.FigureCard("The drive's own estimate",
                    Parts.NumberValue(latest.PercentageUsed ?? 0, v => Math.Round(v).ToString("N0"), "% used"),
                    "Its Percentage Used: whole steps, and it can pass 100", delay + 40),
                Parts.FigureCard("Spare blocks",
                    Parts.NumberValue(latest.AvailableSpare ?? 0, v => Math.Round(v).ToString("N0"), "%",
                        latest.AvailableSpare <= latest.SpareThreshold ? Palette.WarnBrush : null),
                    $"The drive warns at {latest.SpareThreshold}%", delay + 80),
                TemperatureCard(latest, delay + 120)));

            body.Children.Add(Parts.Grid(220,
                Parts.FigureCard("Pace", PaceValue(life), PaceLine(life, d.HistoryDays), delay + 160),
                Parts.FigureCard("Power-on time", Parts.CountValue(latest.PowerOnHours ?? 0, "h"),
                    latest.PowerCycles is { } cycles ? $"Over {Format.Count(cycles)} power cycles" : null, delay + 200),
                Parts.FigureCard("Unsafe shutdowns", Parts.CountValue(latest.UnsafeShutdowns ?? 0),
                    "Power lost without a clean shutdown, over the drive's life", delay + 240),
                Parts.FigureCard("Media errors", Parts.CountValue(latest.MediaErrors ?? 0, null, latest.MediaErrors > 0 ? Palette.WarnBrush : null),
                    latest.MediaErrors > 0 ? "Data the drive could not read back. Back up now." : "Data the drive could not read back", delay + 280)));
        }
        else
        {
            body.Children.Add(Parts.Note(d.HealthRefused is null
                ? "No health log has been read from this drive yet. Temperature, free space and its error log are recorded every hour."
                : _rstInstalled
                    ? "Intel RST does not pass this drive's health log on to a standard account. The RST reader is installed and reads it once a day; its first read has not landed yet (Read now fetches it). Temperature, free space and the error log are recorded every hour."
                    : "Intel RST does not pass this drive's health log on to a standard account, so its wear, data written and spare are not recorded yet. Temperature, free space and the error log are recorded every hour. tools\\Install.ps1 -RstReader adds a daily read through Intel RST (one administrator approval)."));
            body.Children.Add(Parts.Grid(220,
                TemperatureCard(latest, delay),
                Parts.FigureCard("Error log",
                    d.ErrorLog is { Value: > 0 } log ? Parts.CountValue(log.Value, null, size: 32) : Parts.TextValue("Empty"),
                    d.ErrorLog is { Value: > 0 } l2 ? $"The newest entry's number, unchanged since {Format.When(l2.Since, _now, TimeZoneInfo.Local)}. Rejected requests from software add to it; it is not wear." : null,
                    delay + 40)));
        }

        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, HorizontalAlignment = HorizontalAlignment.Right };
        if (d.HealthRefused is not null && _rstInstalled)
        {
            var readNow = ReadNowLink(ctx);
            readNow.VerticalAlignment = VerticalAlignment.Center;
            links.Children.Add(readNow);
        }
        // The way into a drive's history is the panel's one action, so it is the accent button, not a link.
        var open = Ui.Button("Charts and history →", primary: true);
        open.Click += (_, _) => ctx.Navigate(new Route(PageKind.Drive, d.Drive.Id));
        links.Children.Add(open);
        body.Children.Add(links);
        return Ui.Panel(d.Drive.Title, subtitle.Length > 0 ? subtitle : null, Status(d), body);
    }

    private static Border TemperatureCard(LatestReading? latest, int delay)
    {
        var hot = latest?.SensorTemperatureC is { } t && latest.WarningTemperatureC is { } w && t >= w;
        return Parts.FigureCard("Temperature",
            latest?.SensorTemperatureC is { } c ? Parts.NumberValue(c, v => Math.Round(v).ToString("N0"), "°C", hot ? Palette.CautionBrush : null) : Parts.TextValue("Not reported"),
            latest?.WarningTemperatureC is { } warn ? $"The drive warns at {warn} °C" : null, delay);
    }

    private static UIElement PaceValue(LifeEstimate life) => life.Rate30 is { } rate
        ? Parts.NumberValue((double)(rate.TbPerDay * 1000), v => v.ToString("N1"), "GB a day")
        : Parts.TextValue("Not yet");

    private static string PaceLine(LifeEstimate life, int historyDays)
    {
        if (life.Rate30 is null) return $"Needs {Projection.MinimumHistory.TotalDays:0} days of history; {(historyDays < 1 ? "under a day" : historyDays == 1 ? "1 day" : $"{historyDays} days")} so far";
        var span = $"over the last {life.Rate30.SpanDays:0} days";
        if (life.End30 is null) return life.RatedTbw is null ? span : $"{span}; at this pace the rating is more than a century away";
        var range = life.End90 is { } e90 && e90 != life.End30 ? $"{Format.Date(Min(life.End30.Value, e90))} to {Format.Date(Max(life.End30.Value, e90))}" : Format.Date(life.End30.Value);
        return $"{span}. The rating is reached {range} (30- and 90-day pace). TBW is the warranty's limit, not a failure date.";
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    /// <summary>Every volume's free space as a ring, side by side, in the drives' order.</summary>
    private static Border FreeSpacePanel(IReadOnlyList<DriveSummary> drives, int delay)
    {
        var cards = drives.SelectMany(d => d.Volumes.Select(v => VolumeCard(v, d.Drive.Title, delay += 40))).ToArray();
        return Ui.Panel("Free space", null, null, Parts.Grid(340, cards));
    }

    private static Border VolumeCard(VolumeRow v, string drive, int delay)
    {
        var free = v.TotalBytes > 0 ? (double)v.FreeBytes / v.TotalBytes : 0;
        // The ring is how full the volume is: more ink is more used. Amber below 10% free, whatever the size.
        var centre = new StackPanel { Spacing = 0 };
        var percent = Ui.Text(Format.Percent(free * 100), 26, 650, free < 0.1 ? Palette.CautionBrush : Palette.TextBrush, -0.03, numeric: true);
        percent.HorizontalAlignment = HorizontalAlignment.Center;
        var word = Ui.Text("free", 13, 500, Palette.TextMutedBrush);
        word.HorizontalAlignment = HorizontalAlignment.Center;
        centre.Children.Add(percent);
        centre.Children.Add(word);
        var low = free < 0.1;
        var ring = new DonutChart(1 - free, low ? Palette.CautionBrush : Palette.AccentBrush, low ? Palette.CautionDimBrush : Palette.AccentDimBrush, centre);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        text.Children.Add(Ui.Text(v.Letter, 26, 650, spacing: -0.02));
        text.Children.Add(Ui.Text(drive, 15, 500, Palette.TextMutedBrush, wrap: true));
        var bytes = Ui.Text($"{Format.BytesText(v.FreeBytes)} free of {Format.BytesText(v.TotalBytes)}", 15, 400, Palette.TextMutedBrush, numeric: true);
        bytes.Margin = new Thickness(0, 6, 0, 0);
        text.Children.Add(bytes);

        var row = new Grid { ColumnSpacing = 24 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(ring);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var card = Ui.Card(row, new Thickness(22), hover: true);
        Ui.Rise(card, delay);
        return card;
    }

    /// <summary>
    /// A word, not only a colour (Onyx). Red only when something broke:
    /// a warning bit, spare at its threshold, media errors.
    /// </summary>
    public static Border Status(DriveSummary d)
    {
        var (text, kind) = StatusOf(d);
        return Ui.Badge(text, kind);
    }

    private static Ui.BadgeKind Severity(DriveSummary d) => StatusOf(d).Kind;

    public static (string Text, Ui.BadgeKind Kind) StatusOf(DriveSummary d)
    {
        var l = d.Latest;
        if (l is null) return ("No reading", Ui.BadgeKind.Plain);
        if (!l.HasHealth) return ("Partly readable", Ui.BadgeKind.Plain);
        if (l.CriticalWarning > 0 || l.AvailableSpare <= l.SpareThreshold || l.MediaErrors > 0) return ("Needs attention", Ui.BadgeKind.Bad);
        var worn = d.Life?.UsedByWritesPercent >= 80 || l.PercentageUsed >= 80;
        var hot = l.SensorTemperatureC is { } t && l.WarningTemperatureC is { } w && t >= w;
        if (worn || hot) return (worn ? "Wearing" : "Running hot", Ui.BadgeKind.Caution);
        return ("Healthy", Ui.BadgeKind.Ok);
    }
}
