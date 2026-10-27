using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NandWatch.App.Controls;
using NandWatch.App.Theme;
using NandWatch.Core;
using NandWatch.Core.Data;
using NandWatch.Core.Drives;
using NandWatch.Core.Query;
using NandWatch.Core.View;
using NandWatch.Windows.Collect;

namespace NandWatch.App.Views;

/// <param name="Task">The hourly task as Task Scheduler sees it; null when it could not be asked.</param>
public sealed record SyncData(HistorySummary Summary, IReadOnlyList<RunRow> Runs, IReadOnlyList<RefusalRow> Refusals, string DataDir,
    TaskInfo? Task = null, IReadOnlyList<AlertRow>? Alerts = null, TaskInfo? RstTask = null, DateTimeOffset? RstReport = null);

/// <summary>
/// Whether collection is actually running: the last good run, the next, the
/// backup, the run log, and what each drive refused. The history only exists
/// if the collector runs, so a stalled one must be impossible to miss.
/// </summary>
public sealed class SyncPage(PageContext ctx) : IPage
{
    private SyncData? _data;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public void Load()
    {
        _now = DateTimeOffset.UtcNow;
        using var conn = HealthDb.OpenRead(ctx.State.DatabasePath!);
        _data = new SyncData(HealthQueries.Summary(conn), HealthQueries.Runs(conn, 48), HealthQueries.Refusals(conn), ctx.State.DataDir!,
            CollectorTask.Query(), HealthQueries.Alerts(conn, 30), CollectorTask.Query(CollectorTask.RstReaderName), RstReportTime());
    }

    public void Placeholder()
    {
        _now = DateTimeOffset.UtcNow;
        _data = Views.Placeholder.Sync(_now);
    }

    public UIElement Build()
    {
        var d = _data!;
        var zone = TimeZoneInfo.Local;
        var page = new StackPanel();
        page.Children.Add(Parts.PageHead("Sync Status", null));
        // The task is what collects; without it the history stops at the last time the app was open.
        if (d.Task is { Exists: false })
            page.Children.Add(Spaced(Parts.Alert("The hourly task is not registered",
                $"Nothing is collected unless the app is open. Run tools\\Install.ps1 again to register \"{CollectorTask.Name}\".")));
        else if (d.Task is { Enabled: false })
            page.Children.Add(Spaced(Parts.Alert("The hourly task is disabled",
                $"Nothing is collected unless the app is open. Enable \"{CollectorTask.Name}\" in Task Scheduler, or run tools\\Install.ps1 again.")));

        // On schedule within 75 minutes of the last good run; late up to six
        // hours (a laptop asleep); stalled past that while the PC is in use.
        var age = d.Summary.LastOk is { } ok ? _now - ok : (TimeSpan?)null;
        var (state, kind) = age switch
        {
            null => ("No good run yet", Ui.BadgeKind.Bad),
            { TotalMinutes: < 75 } => ("On schedule", Ui.BadgeKind.Ok),
            { TotalHours: < 6 } => ("Late", Ui.BadgeKind.Caution),
            _ => ("Stalled", Ui.BadgeKind.Bad),
        };
        var status = new StackPanel { Spacing = 10 };
        status.Children.Add(Parts.TextValue(d.Summary.LastOk is { } last ? Format.When(last, _now, zone) : "Never"));
        var badge = Ui.Badge(state, kind);
        badge.HorizontalAlignment = HorizontalAlignment.Left; // a StackPanel would stretch it across the card
        status.Children.Add(badge);

        var cards = Parts.Grid(240,
            Parts.FigureCard("Last good run", status, d.Summary.LastOk is { } l ? Format.Ago(l, _now) : null),
            Parts.FigureCard("Next run", Parts.TextValue(NextRun(d, zone)), NextRunLine(d), 40),
            Parts.FigureCard("Backup", Parts.TextValue(d.Summary.LastBackup is { } b ? Format.When(b, _now, zone) : "Never"),
                "Once a day, and on Sync now: one complete file", 80),
            Parts.FigureCard("Readings", Parts.CountValue(d.Summary.Readings, size: 32),
                $"Over {Format.Count(d.Summary.Runs)} {(d.Summary.Runs == 1 ? "run" : "runs")}" + (d.Summary.First is { } first ? $" since {Format.When(first, _now, zone)}" : ""), 120));
        cards.Margin = new Thickness(0, 0, 0, 18.4); // the gap a panel keeps below itself
        page.Children.Add(cards);

        // Alerts before the runs: the run table is long, and an alert is what needs seeing.
        page.Children.Add(Ui.Panel("Alerts", "What the collector raised, newest first, whether or not notifications are on. Red means something broke; amber is a warning.",
            null, AlertsList(d.Alerts ?? [], zone)));
        page.Children.Add(Ui.Panel("Runs", null, null, RunsTable(d.Runs, zone)));
        if (d.RstTask is { Exists: true } || d.Refusals.Any(r => r.Request == Windows.Collect.Collector.HealthRequest))
            page.Children.Add(RstPanel(d, zone));
        if (d.Refusals.Count > 0)
            page.Children.Add(Ui.Panel("Requests a drive refused",
                "Never sent again until the drive's firmware changes: each refused request adds an entry to the drive's own error log.",
                null, RefusalsTable(d.Refusals, zone)));
        page.Children.Add(Ui.Panel("Where things are", null, null, Places(d.DataDir)));
        return page;
    }

    private static DateTimeOffset? RstReportTime()
    {
        try { return File.Exists(AppPaths.RstReport) ? Core.Nvme.RstReport.Parse(File.ReadAllText(AppPaths.RstReport))?.Finished : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// The elevated RST reader, for drives Intel RST will not pass on to a
    /// standard user: shown once such a drive exists, or the reader does.
    /// </summary>
    private Border RstPanel(SyncData d, TimeZoneInfo zone)
    {
        var body = new StackPanel { Spacing = 10 };
        if (d.RstTask is not { Exists: true } t)
        {
            body.Children.Add(Parts.Note("Not installed. tools\\Install.ps1 -RstReader installs it under Program Files with a daily task, behind one administrator approval. Check what it reads first: see the README."));
            return Ui.Panel("Intel RST reader", "Reads the health log of a drive that Intel RST will not pass on to a standard account.", null, body);
        }
        string? Line(string label, string? value) => value is null ? null : $"{label}: {value}";
        foreach (var line in new[]
        {
            Line("Task", t.Enabled ? "installed, daily at 12:37 and on Read now, as SYSTEM" : "installed but disabled in Task Scheduler"),
            Line("Last run", t.LastRun is { } last ? Format.When(last, _now, zone) + (t.LastResult is 0 or 0x41301 ? "" : $", result 0x{t.LastResult:X}") : "never"),
            Line("Next run", t.NextRun is { } next ? Format.When(next, _now, zone) : null),
            Line("Its newest report", d.RstReport is { } r ? Format.When(r, _now, zone) : "none yet"),
        }.OfType<string>())
            body.Children.Add(Ui.Text(line, 15, 400, Palette.TextMutedBrush, wrap: true));
        var read = OverviewPage.ReadNowLink(ctx);
        read.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(read);
        return Ui.Panel("Intel RST reader", "Reads the health log of a drive that Intel RST will not pass on to a standard account: one command, Get Log Page, and nothing else.", null, body);
    }

    private static Border Spaced(Border alert)
    {
        alert.Margin = new Thickness(0, 0, 0, 18.4);
        return alert;
    }

    /// <summary>Task Scheduler's own next run when it can be asked; else the schedule's (seven past the hour).</summary>
    private string NextRun(SyncData d, TimeZoneInfo zone) => d.Task switch
    {
        { Exists: false } => "Not registered",
        { Enabled: false } => "Disabled",
        { NextRun: { } next } => Format.When(next, _now, zone),
        _ => OverviewPage.NextRun(_now).ToString("HH:mm"),
    };

    /// <summary>The schedule, and the task's last result when it was not a success (0) or "still running" (0x41301).</summary>
    private static string NextRunLine(SyncData d) =>
        "Hourly at seven past, and at sign-in" + (d.Task is { Exists: true, LastResult: not 0 and not 0x41301 } t
            ? $". The task's last result was 0x{t.LastResult:X}."
            : "");

    private UIElement AlertsList(IReadOnlyList<AlertRow> alerts, TimeZoneInfo zone)
    {
        if (alerts.Count == 0) return Parts.Note("No alerts. A notification is raised when a drive's health changes for the worse, or collection fails.");
        var list = new StackPanel { Spacing = 14 };
        foreach (var a in alerts)
        {
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(Parts.MonoCell(Format.When(a.At, _now, zone)));
            var badge = a.Level == AlertLevel.Critical ? Ui.Badge("Red", Ui.BadgeKind.Bad) : Ui.Badge("Amber", Ui.BadgeKind.Caution);
            badge.HorizontalAlignment = HorizontalAlignment.Left;
            badge.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
            var text = new StackPanel { Spacing = 3 };
            text.Children.Add(Ui.Text(a.Title, 15, 600, wrap: true));
            text.Children.Add(Ui.Text(a.Body, 14.5, 400, Palette.TextMutedBrush, wrap: true));
            Grid.SetColumn(text, 2);
            row.Children.Add(text);
            list.Children.Add(row);
        }
        return list;
    }

    private UIElement RunsTable(IReadOnlyList<RunRow> runs, TimeZoneInfo zone)
    {
        var table = new DataTable([new Column("Started"), new Column("Trigger", Left: true), new Column("Status", Left: true),
            new Column("Drives"), new Column("Time"), new Column("Backup", Left: true), new Column("Notes", Left: true)]);
        foreach (var r in runs)
        {
            var kind = r.Status switch { "ok" => Ui.BadgeKind.Ok, "partial" => Ui.BadgeKind.Caution, "running" => Ui.BadgeKind.Plain, _ => Ui.BadgeKind.Bad };
            var notes = DataTable.Cell(r.Error ?? "", Palette.TextMutedBrush, numeric: false);
            if (r.Error is { } e) Ui.SetTip(notes, e);
            table.AddRow([
                Parts.MonoCell(Format.When(r.Started, _now, zone)),
                DataTable.Cell(r.Trigger, Palette.TextMutedBrush, numeric: false),
                Ui.Badge(r.Status, kind),
                DataTable.Cell(r.DrivesRead.ToString()),
                DataTable.Cell(r.DurationMs is { } ms ? $"{ms} ms" : "", Palette.TextMutedBrush),
                DataTable.Cell(r.Backup ?? "", r.Backup is { } bk && bk != "ok" ? Palette.WarnBrush : Palette.TextMutedBrush, numeric: false),
                notes,
            ]);
        }
        return table.Build();
    }

    private UIElement RefusalsTable(IReadOnlyList<RefusalRow> refusals, TimeZoneInfo zone)
    {
        var table = new DataTable([new Column("Drive"), new Column("Request", Left: true), new Column("Since"), new Column("Firmware", Left: true), new Column("What Windows said", Left: true)]);
        foreach (var r in refusals)
        {
            // Refusals recorded before 2026-10-24's fix carry Windows' full stop before the "; ".
            var error = r.Error.Replace(".; ", "; ");
            var said = DataTable.Cell(error, Palette.TextMutedBrush, numeric: false);
            Ui.SetTip(said, error);
            table.AddRow([DataTable.Cell(Ratings.DisplayName(r.Model), numeric: false), DataTable.Cell(r.Request, numeric: false), Parts.MonoCell(Format.When(r.First, _now, zone)),
                Parts.MonoCell(r.Firmware), said]);
        }
        return table.Build();
    }

    private static UIElement Places(string dataDir)
    {
        var stack = new StackPanel { Spacing = 12 };
        void Row(string label, string path, string? folder)
        {
            var grid = new Grid { ColumnSpacing = 16 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(Ui.Text(label, 15, 500, Palette.TextMutedBrush));
            var p = Parts.MonoCell(path);
            Grid.SetColumn(p, 1);
            grid.Children.Add(p);
            if (folder is not null)
            {
                var open = Ui.Link("Open", () => Shell.OpenFolder(folder));
                Grid.SetColumn(open, 2);
                grid.Children.Add(open);
            }
            stack.Children.Add(grid);
        }
        Row("History", AppPaths.DatabasePath(dataDir), Path.GetDirectoryName(AppPaths.DatabasePath(dataDir)));
        Row("Backup", AppPaths.BackupPath(dataDir), dataDir);
        Row("Collector log", Path.Combine(AppPaths.LogsDir, "collector.log"), AppPaths.LogsDir);
        return stack;
    }
}
