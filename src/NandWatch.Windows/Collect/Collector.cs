using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using NandWatch.Core;
using NandWatch.Core.Data;
using NandWatch.Core.Drives;
using NandWatch.Core.Nvme;
using NandWatch.Core.Query;
using NandWatch.Core.View;
using NandWatch.Windows.Device;
using NandWatch.Windows.Notify;

namespace NandWatch.Windows.Collect;

/// <param name="Trigger">Why this run: scheduled (the hourly and sign-in task), app, sync or cli. Stored with the run.</param>
/// <param name="ForceBackup">Write the backup even if today's exists (Sync now).</param>
/// <param name="RetryRefused">Forget every remembered refusal and ask again.</param>
public sealed record CollectOptions(string Trigger, bool ForceBackup = false, bool RetryRefused = false);

/// <param name="ExitCode">0 ran (fully or partly) or was skipped; 1 failed; 2 no data folder.</param>
/// <param name="Alerts">What this run raised (kept in the history, and shown as notifications unless they are off).</param>
public sealed record CollectResult(int ExitCode, string Status, int DrivesRead, string? Backup, long DurationMs, IReadOnlyList<string> Notes,
    IReadOnlyList<Alert>? Alerts = null);

/// <summary>
/// One collector run: every NVMe drive read once, unelevated, into the history.
/// The scheduled task runs it hourly and at sign-in; the app and the CLI run
/// it on demand. It only reads drives, and it never repeats a request a drive
/// has refused.
/// </summary>
/// <remarks>
/// <b>It raises the alerts too</b> (<see cref="Alerts"/>: what changed since
/// each drive's previous reading), keeps them in the history and shows them
/// as Windows notifications. A collection that fails raises one alert, not
/// one an hour, until a run succeeds again.
/// </remarks>
public static class Collector
{
    public const string HealthRequest = "health";
    public const string ErrorLogRequest = "error-log";

    /// <summary>
    /// The answers that mean "this drive, through this driver, will not do
    /// that", as opposed to "not now": ERROR_INVALID_FUNCTION and
    /// ERROR_NOT_SUPPORTED. Only these are remembered, so a drive that was
    /// briefly busy is asked again next hour.
    /// </summary>
    private static readonly HashSet<int> Refusals = [1, 50];

    /// <summary>More alerts than this from one run become one summary notification.</summary>
    private const int MaxToasts = 3;

    private const string FailureAlertedKey = "failure_alerted_utc";

    public static CollectResult Run(CollectOptions options)
    {
        var dataDir = AppPaths.DataDir;
        if (dataDir is null)
            return Log(options, new CollectResult(2, "no data folder", 0, null, 0, [$"No data folder is set. Run tools\\Install.ps1, or set {AppPaths.DataDirVariable}."]));

        // One run at a time per data folder: the hourly task, sign-in, the
        // app's Sync now and the CLI can all start one.
        using var mutex = new Mutex(false, MutexName(dataDir));
        bool owned;
        try { owned = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned) return Log(options, new CollectResult(0, "skipped", 0, null, 0, ["Another collection was running."]));
        try
        {
            var result = Log(options, Collect(dataDir, options));
            if (result.Alerts is { Count: > 0 } alerts && Preferences.Notify) Notify(alerts);
            return result;
        }
        finally { mutex.ReleaseMutex(); }
    }

    /// <summary>The alerts as toasts, red first; past <see cref="MaxToasts"/>, the rest in one.</summary>
    private static void Notify(IReadOnlyList<Alert> alerts)
    {
        var ordered = alerts.OrderByDescending(a => a.Level).ToList();
        var shown = ordered.Count <= MaxToasts ? ordered : ordered.Take(MaxToasts - 1).ToList();
        foreach (var a in shown) Toasts.Show(a.Title, a.Body);
        if (shown.Count < ordered.Count)
            Toasts.Show($"{ordered.Count - shown.Count} more NAND Watch alerts",
                string.Join("; ", ordered.Skip(shown.Count).Select(a => a.Title)) + ". See Sync Status in the app.");
    }

    private static CollectResult Collect(string dataDir, CollectOptions options)
    {
        var clock = Stopwatch.StartNew();
        var now = DateTimeOffset.UtcNow;
        var zone = TimeZoneInfo.Local;
        var notes = new List<string>();
        using var conn = HealthDb.Open(AppPaths.DatabasePath(dataDir), AppPaths.AllowSystemDrive);
        if (options.RetryRefused) notes.Add($"Forgot {HealthDb.ClearRefusals(conn)} remembered refusal(s).");
        var runId = HealthDb.StartRun(conn, options.Trigger, now);
        var alerts = new List<Alert>();
        try
        {
            var drives = new Dictionary<int, long>();
            var failed = 0;
            using (var tx = conn.BeginTransaction())
            {
                foreach (var n in Enumerable.Range(0, NvmeReader.MaxDrives).Where(NvmeReader.Exists))
                {
                    var (id, ok) = ReadDrive(conn, runId, n, now, zone, notes, alerts);
                    if (id is { } driveId) drives[n] = driveId;
                    if (!ok) failed++;
                }

                foreach (var v in Volumes.List().Where(v => v.IsDiskVolume))
                {
                    if (v.Disks.Count != 1 || !drives.TryGetValue(v.Disks[0], out var driveId)) continue;
                    HealthDb.InsertVolume(conn, runId, driveId, new VolumeReading(v.Letter, v.Disks[0], v.TotalBytes, v.FreeBytes), now, zone);
                }
                IngestRst(conn, runId, zone, notes, alerts);
                KeepEvents(conn, zone, notes, alerts);
                tx.Commit();
            }

            // The run is finished before the backup copies it, or a restore would show it running for good.
            var status = failed == 0 ? "ok" : drives.Count > 0 ? "partial" : "failed";
            HealthDb.FinishRun(conn, runId, status, drives.Count, null, clock.ElapsedMilliseconds, failed == 0 ? null : string.Join(" ", notes), DateTimeOffset.UtcNow);
            Failure(conn, status == "failed", string.Join(" ", notes), now, alerts);

            string? backup = null;
            if (options.ForceBackup || HealthDb.BackupDue(conn, now, zone))
            {
                HealthDb.Checkpoint(conn);
                backup = HealthDb.Backup(conn, AppPaths.BackupPath(dataDir), now);
                HealthDb.SetRunBackup(conn, runId, backup, clock.ElapsedMilliseconds, DateTimeOffset.UtcNow);
            }
            return new CollectResult(status == "failed" ? 1 : 0, status, drives.Count, backup, clock.ElapsedMilliseconds, notes, alerts);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            notes.Add(ex.Message);
            try
            {
                HealthDb.FinishRun(conn, runId, "failed", 0, null, clock.ElapsedMilliseconds, ex.Message, DateTimeOffset.UtcNow);
                Failure(conn, true, ex.Message, now, alerts);
            }
            catch (SqliteException)
            {
                // The history itself is what failed: the run's line in the log still says so.
            }
            return new CollectResult(1, "failed", 0, null, clock.ElapsedMilliseconds, notes, alerts);
        }
    }

    /// <summary>
    /// A failed collection raises one alert, then stays quiet until a run
    /// succeeds: the hourly task would otherwise repeat it every hour.
    /// </summary>
    private static void Failure(SqliteConnection conn, bool failed, string detail, DateTimeOffset now, List<Alert> alerts)
    {
        var alerted = HealthDb.GetMeta(conn, FailureAlertedKey) is { Length: > 0 };
        if (!failed)
        {
            if (alerted) HealthDb.SetMeta(conn, FailureAlertedKey, "");
            return;
        }
        if (alerted) return;
        var alert = new Alert("collection", AlertLevel.Warning, "NAND Watch could not read the drives",
            (detail.Length > 0 ? detail + " " : "") + "Nothing is recorded until a run succeeds; see Sync Status in the app.");
        HealthDb.InsertAlert(conn, null, alert, now);
        HealthDb.SetMeta(conn, FailureAlertedKey, HealthDb.Iso(now));
        alerts.Add(alert);
    }

    /// <summary>
    /// One drive: identity and temperature from Windows' descriptors, then the
    /// health log and the error log unless the drive refused them before.
    /// Returns the drive's id (null when skipped or failed) and whether it was
    /// handled without failure; a note says what happened.
    /// </summary>
    private static (long? Id, bool Ok) ReadDrive(SqliteConnection conn, long runId, int number, DateTimeOffset now, TimeZoneInfo zone, List<string> notes, List<Alert> alerts)
    {
        using var reader = NvmeReader.Open(number, NvmeReader.NoAccess, out var openError);
        if (reader is null)
        {
            notes.Add($"PhysicalDrive{number}: failed to open ({ReadResult.Failed(openError).Detail}).");
            return (null, false);
        }
        var descriptor = reader.Descriptor(StorageProperty.Device) is { Ok: true } d ? DeviceDescriptor.Parse(d.Data) : null;
        if (descriptor is null)
        {
            notes.Add($"PhysicalDrive{number}: Windows gave no device descriptor.");
            return (null, false);
        }
        if (descriptor.BusType != StorageBus.Nvme)
        {
            notes.Add($"PhysicalDrive{number}: {descriptor.Product} is on {descriptor.BusName}, not NVMe; not supported yet.");
            return (null, true);
        }

        var identity = new DriveIdentity(descriptor.Product, descriptor.Serial, descriptor.Revision);
        var driveId = HealthDb.UpsertDrive(conn, identity, now);
        var temperature = reader.Descriptor(StorageProperty.DeviceTemperature) is { Ok: true } t ? TemperatureDescriptor.Parse(t.Data) : null;

        var healthReply = Ask(conn, reader, driveId, identity, HealthRequest, NvmeRead.HealthLog, now, notes);
        var health = healthReply is null ? null : HealthLog.TryParse(healthReply);
        var errorReply = Ask(conn, reader, driveId, identity, ErrorLogRequest, NvmeRead.StandardLogs[0], now, notes);
        var errors = errorReply is null ? null : ErrorLogEntry.Parse(errorReply);

        // The previous reading, for what changed since (the alerts).
        var before = HealthQueries.Latest(conn, driveId);
        var writtenBefore = HealthQueries.LatestWrittenUnits(conn, driveId);

        HealthDb.InsertReading(conn, runId, driveId, new DriveReading
        {
            Drive = identity,
            DiskNumber = number,
            At = now,
            Health = health,
            HealthPage = health is null ? null : healthReply,
            SensorTemperatureC = temperature?.TemperatureC,
            WarningTemperatureC = temperature?.EffectiveWarningC,
            ErrorEntries = errors,
        }, zone);
        var newErrors = errors is null ? 0 : HealthDb.RecordMediaErrors(conn, driveId, errors, now);
        if (newErrors > 0) notes.Add($"{identity.Model}: {newErrors} new media or I/O error entr{(newErrors == 1 ? "y" : "ies")}.");

        var drive = HealthQueries.Drives(conn).First(x => x.Id == driveId);
        decimal? Used(long? units) => units is { } u && drive.RatedTbw is > 0 ? HealthLog.Terabytes((UInt128)u) / drive.RatedTbw.Value * 100 : null;
        var after = HealthQueries.Latest(conn, driveId)!;
        var raised = Alerts.Compare(drive.Title, before, after, Used(writtenBefore),
            Used(health is null ? null : (long)UInt128.Min(health.DataUnitsWritten, (UInt128)long.MaxValue)), newErrors);
        foreach (var a in raised) HealthDb.InsertAlert(conn, driveId, a, now);
        alerts.AddRange(raised);
        return (driveId, true);
    }

    private const string RstIngestedKey = "rst_ingested_utc";
    private const string EventsThroughKey = "events_through_utc";

    /// <summary>
    /// Copies the System log's storage and shutdown events since the newest
    /// one kept (on the first run, everything the log still holds), so they
    /// outlive the log's rollover and a Windows reset. A log that cannot be
    /// read is a note, never a failed run.
    /// </summary>
    /// <remarks>
    /// New errors (level 1 or 2) on a disk that is one of the drives read here
    /// raise one red alert per drive per run. The first copy of the log raises
    /// none: it is old news, and the Events page shows it.
    /// </remarks>
    private static void KeepEvents(SqliteConnection conn, TimeZoneInfo zone, List<string> notes, List<Alert> alerts)
    {
        var since = HealthDb.GetMeta(conn, EventsThroughKey) is { Length: > 0 } t ? HealthDb.ParseIso(t) : (DateTimeOffset?)null;
        List<SystemEvent> events;
        try { events = SystemLog.Since(since); }
        catch (Exception ex) when (ex is System.Diagnostics.Eventing.Reader.EventLogException or UnauthorizedAccessException)
        {
            notes.Add($"The System log was not read: {ex.Message.Trim()}");
            return;
        }
        var added = events.Where(e => HealthDb.InsertEvent(conn, e, zone)).ToList();
        if (events.Count > 0) HealthDb.SetMeta(conn, EventsThroughKey, HealthDb.Iso(events[^1].At));
        if (added.Count == 0 || since is null) return;
        notes.Add($"{added.Count} new storage or shutdown event(s) in the System log.");

        var disks = HealthQueries.DiskNumbers(conn);
        var drives = HealthQueries.Drives(conn).ToDictionary(d => d.Id);
        foreach (var group in added.Where(e => e.Level is 1 or 2 && e.Disk is { } n && disks.ContainsKey(n)).GroupBy(e => disks[e.Disk!.Value]))
        {
            var kinds = string.Join(", ", group.GroupBy(e => SystemEvents.Describe(e.Provider, e.Id)).Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ({g.Count()})"));
            var alert = new Alert("storage-events", AlertLevel.Critical, $"{drives[group.Key].Title}: Windows logged storage errors",
                $"{kinds}. See Events in the app; back up what matters on the drive.");
            HealthDb.InsertAlert(conn, group.Key, alert, group.Max(e => e.At));
            alerts.Add(alert);
        }
    }

    /// <summary>
    /// The elevated RST reader's newest report, once: each page becomes a
    /// reading (source "rst") at the time the reader ran, for a drive whose
    /// own health log this collector cannot read (a remembered refusal). A
    /// drive it reads itself gains nothing from a daily copy. A page that does
    /// not look like a health log is noted and left out.
    /// </summary>
    private static void IngestRst(SqliteConnection conn, long runId, TimeZoneInfo zone, List<string> notes, List<Alert> alerts)
    {
        RstReport? report;
        try { report = File.Exists(AppPaths.RstReport) ? RstReport.Parse(File.ReadAllText(AppPaths.RstReport)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        if (report is null) return;
        if (HealthDb.GetMeta(conn, RstIngestedKey) is { Length: > 0 } done && HealthDb.ParseIso(done) >= report.Finished) return;
        HealthDb.SetMeta(conn, RstIngestedKey, HealthDb.Iso(report.Finished));

        // First the proof: every drive this collector also reads itself must
        // agree with its own log. One that does not, and the whole report is
        // left out: the pass-through cannot be trusted for the others either.
        if (RstCrossCheck(conn, report) is { } mismatch)
        {
            notes.Add($"The RST reader's report was not used: {mismatch}.");
            var alert = new Alert("rst-mismatch", AlertLevel.Warning, "The RST reader disagreed with a drive's own health log",
                $"{mismatch}. Its report was not used. See Sync Status in the app.");
            HealthDb.InsertAlert(conn, null, alert, report.Finished);
            alerts.Add(alert);
            return;
        }

        foreach (var d in report.Drives)
        {
            if (d.Device is not { } device) continue;
            var identity = new DriveIdentity(device.Product, device.Serial, device.Revision);
            if (HealthQueries.DriveId(conn, identity.Model, identity.DeviceId) is not { } driveId) continue;
            if (!HealthDb.IsRefused(conn, driveId, HealthRequest, identity.Firmware)) continue;
            if (RstReport.Implausible(d) is { } why)
            {
                notes.Add($"{identity.Model}: the RST reader's page was not used ({why}).");
                continue;
            }
            var before = HealthQueries.Latest(conn, driveId);
            var writtenBefore = HealthQueries.LatestWrittenUnits(conn, driveId);
            var health = d.Health!;
            HealthDb.InsertReading(conn, runId, driveId, new DriveReading
            {
                Drive = identity,
                DiskNumber = d.Disk,
                At = report.Started,
                Health = health,
                HealthPage = d.Page,
                Source = DriveReading.RstSource,
            }, zone);
            var drive = HealthQueries.Drives(conn).First(x => x.Id == driveId);
            decimal? Used(long? units) => units is { } u && drive.RatedTbw is > 0 ? HealthLog.Terabytes((UInt128)u) / drive.RatedTbw.Value * 100 : null;
            var raised = Alerts.Compare(drive.Title, before, HealthQueries.Latest(conn, driveId)!, Used(writtenBefore),
                Used((long)UInt128.Min(health.DataUnitsWritten, (UInt128)long.MaxValue)));
            foreach (var a in raised) HealthDb.InsertAlert(conn, driveId, a, report.Finished);
            alerts.AddRange(raised);
            notes.Add($"{identity.Model}: health log through the RST reader, {report.Started.ToLocalTime():yyyy-MM-dd HH:mm}.");
        }
    }

    /// <summary>
    /// The reader's page for each drive this collector reads itself, against
    /// its own reading within two hours. Null when every such drive agrees (or
    /// none could be compared); else which drive and what differs.
    /// </summary>
    public static string? RstCrossCheck(SqliteConnection conn, RstReport report)
    {
        foreach (var d in report.Drives)
        {
            if (d.Device is not { } device || d.Health is not { } rst) continue;
            if (HealthQueries.DriveId(conn, device.Product, device.Serial) is not { } id) continue;
            if (HealthDb.IsRefused(conn, id, HealthRequest, device.Revision)) continue;
            if (HealthQueries.OwnHealthNear(conn, id, report.Started, TimeSpan.FromHours(2)) is not { } own) continue;
            if (RstReport.CrossCheck(rst, own.Health) is { } why) return $"{device.Product}: {why}";
        }
        return null;
    }

    /// <summary>
    /// One NVMe read, device scope then adapter scope, unless this drive has
    /// refused it on its current firmware. A definitive refusal is remembered
    /// (each attempt adds an entry to the drive's own error log); anything
    /// else is a failed read for this run only.
    /// </summary>
    private static byte[]? Ask(SqliteConnection conn, NvmeReader reader, long driveId, DriveIdentity drive, string request, NvmeRead read, DateTimeOffset now, List<string> notes)
    {
        if (HealthDb.IsRefused(conn, driveId, request, drive.Firmware)) return null;
        var device = reader.Read(StorageProperty.DeviceProtocolSpecific, read);
        if (device.Ok) return device.Data;
        var adapter = reader.Read(StorageProperty.AdapterProtocolSpecific, read);
        if (adapter.Ok) return adapter.Data;
        if (Refusals.Contains(device.Error) && Refusals.Contains(adapter.Error))
        {
            HealthDb.RecordRefusal(conn, driveId, request, drive.Firmware, $"device: {device.Detail}; adapter: {adapter.Detail}", now);
            notes.Add($"{drive.Model}: refuses its {request} through this driver; not asked again on firmware {drive.Firmware}.");
        }
        else notes.Add($"{drive.Model}: {request} not read this time (device: {device.Detail}; adapter: {adapter.Detail}).");
        return null;
    }

    private static string MutexName(string dataDir)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDir).ToUpperInvariant()));
        return @"Local\NandWatch.Collector-" + Convert.ToHexString(hash, 0, 8);
    }

    /// <summary>
    /// One line per run in logs\collector.log, UTF-8. Rolled to
    /// collector.old.log past 1 MB (years of hourly lines), so it never grows
    /// without bound on the system drive.
    /// </summary>
    private static CollectResult Log(CollectOptions options, CollectResult result)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            var file = Path.Combine(AppPaths.LogsDir, "collector.log");
            if (File.Exists(file) && new FileInfo(file).Length > 1_000_000)
                File.Move(file, Path.Combine(AppPaths.LogsDir, "collector.old.log"), overwrite: true);
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {options.Trigger} {result.Status}, {result.DrivesRead} drive(s), backup {result.Backup ?? "not due"}, {result.DurationMs} ms");
            if (result.Notes.Count > 0) line += " | " + string.Join(" ", result.Notes);
            line += Environment.NewLine;
            File.AppendAllText(file, line, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A log that cannot be written must not fail the collection.
        }
        return result;
    }
}
