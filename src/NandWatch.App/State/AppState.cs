using Microsoft.Data.Sqlite;
using NandWatch.Core;
using NandWatch.Core.Data;
using NandWatch.Core.Query;
using NandWatch.Windows.Collect;

namespace NandWatch.App.State;

/// <summary>
/// What the window knows about the history: where it is, whether it holds
/// anything, and when it changed. Pages read the database themselves
/// (IPage.Load, off the UI thread); this only says when to rebuild them.
/// </summary>
/// <remarks>
/// <b>Live refresh</b> (Data Usage Native's): the live database's files are
/// watched, and 3 s after the last write the history's signature (readings,
/// the newest run and its state) is read. Only a change rebuilds the
/// page, so a checkpoint or the app's own read does not flash anything twice.
/// </remarks>
public sealed class AppState : IDisposable
{
    private FileSystemWatcher? _watcher;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _debounce;
    private string? _signature;

    public AppState() => Open();

    public string? DataDir { get; private set; }

    public string? DatabasePath => DataDir is null ? null : AppPaths.DatabasePath(DataDir);

    /// <summary>The history has at least one reading.</summary>
    public bool HasData { get; private set; }

    /// <summary>The newest good run, for the stall check.</summary>
    public DateTimeOffset? LastOk { get; private set; }

    /// <summary>Raised on the UI thread after a reload: pages rebuild.</summary>
    public event Action? Changed;

    /// <summary>Re-reads where the data folder is (after the setup screen chose one).</summary>
    public void Open() => DataDir = AppPaths.DataDir;

    public async Task ReloadAsync()
    {
        var path = DatabasePath;
        var (hasData, lastOk, signature) = await Task.Run(() =>
        {
            if (path is null || !File.Exists(path)) return (false, (DateTimeOffset?)null, (string?)null);
            using var conn = HealthDb.OpenRead(path);
            return (HealthDb.CountReadings(conn) > 0, HealthQueries.Summary(conn).LastOk, Signature(conn));
        });
        HasData = hasData;
        LastOk = lastOk;
        _signature = signature;
        Changed?.Invoke();
    }

    /// <summary>
    /// One collector run, in this process, off the UI thread: the same code the
    /// hourly task runs. "app" on opening, "sync" for Sync now (which also
    /// writes the backup).
    /// </summary>
    public async Task<CollectResult?> CollectAsync(string trigger, bool backup)
    {
        if (DataDir is null) return null;
        var result = await Task.Run(() => Collector.Run(new CollectOptions(trigger, ForceBackup: backup)));
        await ReloadAsync();
        return result;
    }

    /// <summary>
    /// Read now, for a drive behind Intel RST: starts the elevated reader's
    /// task (no prompt: the user may run it), waits for a newer report (at
    /// most a minute; a run takes a second or two), then collects, which
    /// folds the report into the history. Null when it worked, else why not.
    /// </summary>
    public async Task<string?> ReadThroughRstAsync()
    {
        var before = ReportTime();
        if (CollectorTask.Run(CollectorTask.RstReaderName) is { } why) return why;
        for (var i = 0; i < 60 && ReportTime() == before; i++) await Task.Delay(1000);
        if (ReportTime() == before) return "The RST reader did not finish within a minute. See its task in Task Scheduler.";
        var result = await CollectAsync("rst", backup: false);
        return result is { ExitCode: 0 } ? null : result?.Status ?? "No data folder is set.";
    }

    private static DateTimeOffset? ReportTime()
    {
        try
        {
            return File.Exists(AppPaths.RstReport) ? Core.Nvme.RstReport.Parse(File.ReadAllText(AppPaths.RstReport))?.Finished : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Watches the live database; call on the UI thread, again after the data folder changes.</summary>
    public void Watch(Microsoft.UI.Dispatching.DispatcherQueue ui)
    {
        _watcher?.Dispose();
        _watcher = null;
        if (DatabasePath is not { } db || Path.GetDirectoryName(db) is not { } live || !Directory.Exists(live)) return;
        if (_debounce is null)
        {
            _debounce = ui.CreateTimer();
            _debounce.Interval = TimeSpan.FromSeconds(3);
            _debounce.IsRepeating = false;
            _debounce.Tick += async (_, _) => await RefreshIfChangedAsync();
        }
        var debounce = _debounce;
        _watcher = new FileSystemWatcher(live, "nand-watch.db*")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        void Poke(object? sender, FileSystemEventArgs e) => ui.TryEnqueue(() =>
        {
            debounce.Stop();
            debounce.Start();
        });
        _watcher.Changed += Poke;
        _watcher.Created += Poke;
    }

    private async Task RefreshIfChangedAsync()
    {
        if (DatabasePath is not { } path || !File.Exists(path)) return;
        try
        {
            var signature = await Task.Run(() =>
            {
                using var conn = HealthDb.OpenRead(path);
                return Signature(conn);
            });
            if (signature != _signature) await ReloadAsync();
        }
        catch (Exception ex) when (ex is SqliteException or IOException)
        {
            // Mid-write: the next write pokes again.
        }
    }

    private static string Signature(SqliteConnection conn) => HealthQueries.Signature(conn);

    /// <summary>A line per drive for the tray's tooltip: "Micron 2400 512 GB: Healthy".</summary>
    public async Task<IReadOnlyList<string>> TrayLinesAsync()
    {
        if (DatabasePath is not { } path || !File.Exists(path)) return ["No history yet"];
        return await Task.Run(() =>
        {
            using var conn = HealthDb.OpenRead(path);
            return (IReadOnlyList<string>)Views.OverviewPage.Summaries(conn, DateTimeOffset.UtcNow)
                .Select(d => $"{d.Drive.Title}: {Views.OverviewPage.StatusOf(d).Text}").ToList();
        });
    }

    public void Dispose() => _watcher?.Dispose();
}
