using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NandWatch.App.Controls;
using NandWatch.App.Theme;
using NandWatch.Core.Data;
using NandWatch.Core.Query;
using NandWatch.Core.View;

namespace NandWatch.App.Views;

public sealed record EventsData(IReadOnlyList<DriveRow> Drives, IReadOnlyDictionary<int, long> Disks, IReadOnlyList<EventGroup> Storage, IReadOnlyList<ShutdownDay> Shutdowns);

/// <summary>
/// What Windows logged about the drives: storage warnings and errors grouped
/// by disk, and unexpected shutdowns by day beside each drive's own count.
/// The collector copies them from the System log every hour, so they outlive
/// the log's rollover and a Windows reset.
/// </summary>
public sealed class EventsPage(PageContext ctx) : IPage
{
    /// <summary>Days of unsafe shutdowns shown, newest first.</summary>
    private const int ShutdownRows = 60;

    private EventsData? _d;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public void Load()
    {
        _now = DateTimeOffset.UtcNow;
        using var conn = HealthDb.OpenRead(ctx.State.DatabasePath!);
        var drives = HealthQueries.Drives(conn);
        _d = new EventsData(drives, HealthQueries.DiskNumbers(conn), HealthQueries.StorageEvents(conn), HealthQueries.UnsafeShutdowns(conn, drives.Select(d => d.Id)));
    }

    public void Placeholder()
    {
        _now = DateTimeOffset.UtcNow;
        var drive = Views.Placeholder.Drives(_now)[0].Drive;
        _d = new EventsData([drive], new Dictionary<int, long> { [0] = drive.Id },
            [new EventGroup(0, "disk", 153, 3, 4, _now.AddDays(-20), _now.AddDays(-2))],
            Enumerable.Range(0, 5).Select(i => new ShutdownDay(DateOnly.FromDateTime(_now.LocalDateTime).AddDays(-i * 7), 1, new Dictionary<long, long> { [drive.Id] = 1 })).ToList());
    }

    public UIElement Build()
    {
        var d = _d!;
        var zone = TimeZoneInfo.Local;
        var page = new StackPanel();
        page.Children.Add(Parts.PageHead("Events",
            "What Windows logged about the drives, copied from its System log every hour so it outlives the log's rollover and a reset. Counts only: no message text is kept."));
        page.Children.Add(StoragePanel(d, zone));
        page.Children.Add(ShutdownPanel(d));
        return page;
    }

    private string DiskName(EventsData d, int? disk) => disk switch
    {
        null => "No disk named",
        { } n when d.Disks.TryGetValue(n, out var id) && d.Drives.FirstOrDefault(x => x.Id == id) is { } drive => $"{drive.Title} (disk {n})",
        { } n => $"Disk {n}",
    };

    private Border StoragePanel(EventsData d, TimeZoneInfo zone)
    {
        const string subtitle = "Warnings and errors from the disk, controller and file-system drivers, by the disk each names. Disk numbers are Windows': a disk that is not one of the drives above was removable, or is no longer attached.";
        if (d.Storage.Count == 0)
            return Ui.Panel("Storage errors and warnings", subtitle, null, Parts.Note("None kept: Windows has logged no storage warning or error for as far back as its log reached."));
        var body = new StackPanel { Spacing = 22 };
        foreach (var disk in d.Storage.GroupBy(g => g.Disk))
        {
            var block = new StackPanel { Spacing = 8 };
            var head = Ui.Text(DiskName(d, disk.Key), 16, 600);
            block.Children.Add(head);
            if (disk.Key is { } n && !d.Disks.ContainsKey(n))
                block.Children.Add(Parts.Note("Not one of the drives this app reads: removable, or no longer attached."));
            var table = new DataTable([new Column("Event"), new Column("Level", Left: true), new Column("Count"), new Column("First"), new Column("Last")]);
            foreach (var g in disk)
            {
                var badge = g.Level <= 2 ? Ui.Badge(SystemEvents.Level(g.Level), Ui.BadgeKind.Bad) : Ui.Badge(SystemEvents.Level(g.Level), Ui.BadgeKind.Caution);
                badge.HorizontalAlignment = HorizontalAlignment.Left;
                var what = DataTable.Cell(SystemEvents.Describe(g.Provider, g.Id), numeric: false);
                Ui.SetTip(what, $"{g.Provider}, event {g.Id}");
                table.AddRow([what, badge, DataTable.Cell(Format.Count(g.Count)),
                    Parts.MonoCell(Format.When(g.First, _now, zone)), Parts.MonoCell(Format.When(g.Last, _now, zone))]);
            }
            block.Children.Add(table.Build());
            body.Children.Add(block);
        }
        return Ui.Panel("Storage errors and warnings", subtitle, null, body);
    }

    private Border ShutdownPanel(EventsData d)
    {
        const string subtitle = "By the day each was seen, which is the next start. Windows: it did not shut down cleanly (Kernel-Power 41, or EventLog 6008). A drive: it lost power without being told first, from its own Unsafe Shutdowns counter. A drive's count with no Windows record often means power was cut while Windows slept or hibernated.";
        if (d.Shutdowns.Count == 0)
            return Ui.Panel("Unsafe shutdowns", subtitle, null, Parts.Note("None recorded since the history began."));
        // A column per drive whose counter is read (its health log); others have none.
        var counted = d.Drives.Where(x => d.Shutdowns.Any(s => s.Drives.ContainsKey(x.Id))).ToList();
        var columns = new List<Column> { new("Day"), new("Windows") };
        columns.AddRange(counted.Select(x => new Column(x.Title)));
        var table = new DataTable(columns);
        foreach (var day in d.Shutdowns.Take(ShutdownRows))
        {
            var cells = new List<UIElement?>
            {
                Parts.MonoCell(Format.DayLong(day.Date)),
                DataTable.Cell(day.Windows > 0 ? Format.Count(day.Windows) : "–", day.Windows > 0 ? null : Palette.TextFaintBrush),
            };
            cells.AddRange(counted.Select(x => (UIElement?)DataTable.Cell(day.Drives.TryGetValue(x.Id, out var n) ? "+" + Format.Count(n) : "–", day.Drives.ContainsKey(x.Id) ? null : Palette.TextFaintBrush)));
            table.AddRow(cells);
        }
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(table.Build());
        if (d.Shutdowns.Count > ShutdownRows) body.Children.Add(Parts.Note($"The newest {ShutdownRows} days of {d.Shutdowns.Count}."));
        return Ui.Panel("Unsafe shutdowns", subtitle, null, body);
    }
}
