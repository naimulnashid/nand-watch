using System.Globalization;
using NandWatch.Core;
using NandWatch.Core.Data;
using NandWatch.Core.Nvme;
using NandWatch.Core.Query;
using NandWatch.Windows.Collect;
using NandWatch.Windows.Probe;

namespace NandWatch.Cli;

/// <summary>collect, stats and where: the collector by hand, and what the history holds.</summary>
internal static class HistoryCommands
{
    public static int Collect(string[] args)
    {
        var i = Array.IndexOf(args, "--trigger");
        var result = Collector.Run(new CollectOptions(i >= 0 && i + 1 < args.Length ? args[i + 1] : "cli",
            args.Contains("--backup"), args.Contains("--retry-refused")));
        Console.WriteLine($"{result.Status}: {result.DrivesRead} drive(s) read, backup {result.Backup ?? "not due"}, {result.DurationMs} ms");
        foreach (var note in result.Notes) Console.WriteLine("  " + note);
        return result.ExitCode;
    }

    public static int Where()
    {
        var location = AppPaths.ReadLocation();
        var dataDir = AppPaths.DataDir;
        Console.WriteLine($"Data folder      {dataDir ?? "(not set)"}{(Environment.GetEnvironmentVariable(AppPaths.DataDirVariable) is { Length: > 0 } ? $"  (from {AppPaths.DataDirVariable})" : location.DataDir is not null ? "  (from location.json)" : "")}");
        if (dataDir is not null)
        {
            Console.WriteLine($"Database         {AppPaths.DatabasePath(dataDir)}");
            Console.WriteLine($"Backup           {AppPaths.BackupPath(dataDir)}");
        }
        Console.WriteLine($"Local folder     {AppPaths.LocalDir}");
        Console.WriteLine($"location.json    {AppPaths.LocationFile}{(File.Exists(AppPaths.LocationFile) ? "" : "  (missing)")}");
        Console.WriteLine($"Collector log    {Path.Combine(AppPaths.LogsDir, "collector.log")}");
        return 0;
    }

    public static int Alerts(string[] args)
    {
        var dataDir = AppPaths.DataDir ?? throw new InvalidOperationException($"No data folder is set. Run tools\\Install.ps1, or set {AppPaths.DataDirVariable}.");
        var path = AppPaths.DatabasePath(dataDir);
        if (!File.Exists(path)) throw new InvalidOperationException($"No history yet at {path}. Run: nandwatch collect");
        var count = Program.Arg(args, "count") is { } n ? int.Parse(n, CultureInfo.InvariantCulture) : 20;
        using var conn = HealthDb.OpenRead(path);
        var alerts = HealthQueries.Alerts(conn, count);
        if (alerts.Count == 0) Console.WriteLine("No alerts.");
        foreach (var a in alerts)
            Console.WriteLine($"{TimeZoneInfo.ConvertTime(a.At, TimeZoneInfo.Local):yyyy-MM-dd HH:mm}  {(a.Level == AlertLevel.Critical ? "RED  " : "AMBER")}  {a.Title}. {a.Body}");
        return 0;
    }

    public static int RstShow(string[] args)
    {
        var file = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? AppPaths.RstReport;
        if (!File.Exists(file)) throw new InvalidOperationException($"No RST report at {file}. Run NandWatchRstReader.exe --out <folder> from an elevated terminal first.");
        var report = RstReport.Parse(File.ReadAllText(file)) ?? throw new InvalidOperationException($"{file} is not an RST reader report.");
        Console.WriteLine($"Read {report.Started.ToLocalTime():yyyy-MM-dd HH:mm:ss}, {report.Drives.Count} NVMe drive(s).");
        // The proof the pass-through returns real health logs: a drive this
        // app also reads itself must agree with its own reading.
        var dataDir = AppPaths.DataDir;
        var db = dataDir is null ? null : AppPaths.DatabasePath(dataDir);
        using var conn = db is not null && File.Exists(db) ? HealthDb.OpenRead(db) : null;
        foreach (var d in report.Drives)
        {
            Console.WriteLine();
            Console.WriteLine($"PhysicalDrive{d.Disk}: {d.Device?.Product ?? "?"}  (id {MachineInfo.Mask(d.Device?.Serial ?? "")}, firmware {d.Device?.Revision ?? "?"})");
            Console.WriteLine($"  {"Pass-through",-38} {(d.Ok ? "answered" : $"failed: {d.Detail ?? d.Error.ToString(CultureInfo.InvariantCulture)}")}");
            Console.WriteLine($"  {"SRB return code / completion dwords",-38} 0x{d.SrbReturnCode:X8} / {d.CompletionText}");
            Console.WriteLine($"  {"Plausible",-38} {(RstReport.Implausible(d) is { } why ? "no: " + why : "yes")}");
            if (conn is not null && d.Device is { } device && d.Health is { } rst && HealthQueries.DriveId(conn, device.Product, device.Serial) is { } id)
            {
                var refused = HealthDb.IsRefused(conn, id, Collector.HealthRequest, device.Revision);
                var own = refused ? null : HealthQueries.OwnHealthNear(conn, id, report.Started, TimeSpan.FromHours(2));
                Console.WriteLine($"  {"Against the drive's own health log",-38} " + (refused
                    ? "none: it refuses its own, so the collector uses this one"
                    : own is not { } o ? "no reading of its own within two hours"
                    : RstReport.CrossCheck(rst, o.Health) is { } why2 ? "DISAGREES: " + why2 : $"agrees (its own reading at {o.At.ToLocalTime():HH:mm})"));
            }
            if (d.Health is { } h)
                foreach (var (name, value) in h.Fields()) Console.WriteLine($"  {name,-38} {value}");
        }
        return 0;
    }

    public static int NotifyTest()
    {
        var shown = NandWatch.Windows.Notify.Toasts.Show("NAND Watch notifications work", "This is a test. Alerts look like this; a click opens the app.");
        Console.WriteLine(shown
            ? "Windows accepted the notification. If none appeared, check Settings > System > Notifications (NAND Watch, and Do not disturb)."
            : "Windows refused the notification.");
        Console.WriteLine($"Notifications in the app's settings: {(Preferences.Notify ? "on" : "off")} (the collector honours it; this test does not).");
        return shown ? 0 : 1;
    }

    public static int Stats(string[] args)
    {
        var dataDir = AppPaths.DataDir ?? throw new InvalidOperationException($"No data folder is set. Run tools\\Install.ps1, or set {AppPaths.DataDirVariable}.");
        var path = AppPaths.DatabasePath(dataDir);
        if (!File.Exists(path)) throw new InvalidOperationException($"No history yet at {path}. Run: nandwatch collect");
        var days = Program.Arg(args, "days") is { } n ? int.Parse(n, CultureInfo.InvariantCulture) : 14;
        var now = DateTimeOffset.UtcNow;
        var zone = TimeZoneInfo.Local;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

        using var conn = HealthDb.OpenRead(path);
        var backup = HealthDb.GetMeta(conn, "last_backup_utc");
        Console.WriteLine($"History: {path} ({new FileInfo(path).Length / 1024.0:N0} KB), last backup {(backup is null ? "never" : Local(HealthDb.ParseIso(backup), zone))}");
        Console.WriteLine();

        var refusals = HealthQueries.Refusals(conn);
        foreach (var drive in HealthQueries.Drives(conn))
        {
            Console.WriteLine($"{drive.Label ?? drive.Model}  (id {MachineInfo.Mask(drive.DeviceId)}, firmware {drive.Firmware})");
            var latest = HealthQueries.Latest(conn, drive.Id);
            if (latest is null) { Console.WriteLine("  no readings yet"); Console.WriteLine(); continue; }
            Console.WriteLine($"  {"Last reading",-16} {Local(latest.At, zone)}, {Temp(latest.SensorTemperatureC)} (warning {Temp(latest.WarningTemperatureC)})");
            if (HealthQueries.ErrorLogSince(conn, drive.Id) is { } log)
                Console.WriteLine(log.Value == 0
                    ? $"  {"Error log",-16} empty"
                    : $"  {"Error log",-16} newest entry #{log.Value:N0}, unchanged since {Local(log.Since, zone)}");

            if (!latest.HasHealth)
            {
                var refused = refusals.FirstOrDefault(r => r.Model == drive.Model && r.Request == Collector.HealthRequest);
                Console.WriteLine($"  {"Health log",-16} not readable{(refused is null ? "" : $" through this driver (refused since {Local(refused.First, zone)})")}; temperature and free space only");
            }
            else
            {
                var warn = latest.CriticalWarning is > 0 ? $"WARNING BITS 0x{latest.CriticalWarning:x2}" : "no warning bits";
                Console.WriteLine($"  {"Health",-16} spare {latest.AvailableSpare}% (threshold {latest.SpareThreshold}%), {warn}, {latest.MediaErrors} media errors, " +
                                  $"{latest.UnsafeShutdowns} unsafe shutdowns, {latest.PowerOnHours:N0} h on, {latest.WarningTemperatureMinutes} min above warning temperature");
                var written = HealthQueries.Written(conn, drive.Id);
                var life = Projection.Estimate(written, latest.PercentageUsed, drive.RatedTbw, now, zone);
                Console.WriteLine($"  {"Life used",-16} {life.WrittenTb:0.00} TB written" +
                                  (life.RatedTbw is { } rated ? $" of {NandWatch.Core.View.Format.Tbw(rated)} = {life.UsedByWritesPercent:0.0}% ({drive.RatingSource})" : " (no rating known for this model)") +
                                  $"; the drive's own estimate {life.VendorPercentUsed}%");
                Console.WriteLine($"  {"Pace",-16} {Pace("30 days", life.Rate30, life.End30)}; {Pace("90 days", life.Rate90, life.End90)}");

                // From the first reading at most: before it there is no history, not a gap.
                var from = today.AddDays(1 - days);
                if (written.Count > 0 && written[0].LocalDate > from) from = written[0].LocalDate;
                var series = WriteHistory.Daily(written, from, today, HealthQueries.HealthInterval(conn, drive.Id));
                Console.WriteLine($"  {"Written per day",-16} " + string.Join("  ", series.Days.Select(d => $"{d.Date:MM-dd} {Day(d)}")));
                foreach (var reset in series.Resets) Console.WriteLine($"  counter reset at {Local(reset.At, zone)}: {reset.From} -> {reset.To}");
            }

            foreach (var v in HealthQueries.LatestVolumes(conn, drive.Id))
                Console.WriteLine($"  {v.Letter,-16} {100.0 * v.FreeBytes / v.TotalBytes:0.0}% free of {v.TotalBytes / 1e9:0.0} GB");
            Console.WriteLine();
        }

        Console.WriteLine("Recent runs");
        foreach (var run in HealthQueries.Runs(conn, 8))
            Console.WriteLine($"  {Local(run.Started, zone)}  {run.Trigger,-7} {run.Status,-8} {run.DrivesRead} drive(s), {run.DurationMs} ms" +
                              (run.Backup is null ? "" : $", backup {run.Backup}") + (run.Error is null ? "" : $"  {run.Error}"));
        return 0;
    }

    private static string Local(DateTimeOffset t, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(t, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Temp(short? c) => c is null ? "-" : $"{c} C";

    private static string Pace(string window, WriteRate? rate, DateOnly? end) => rate is null
        ? $"over {window}: needs {Projection.MinimumHistory.TotalDays:0} days of history"
        : $"{rate.TbPerDay * 1000:0.0} GB/day over {rate.SpanDays:0} days" + (end is { } e ? $", rating reached {e:yyyy-MM-dd}" : "");

    private static string Day(DayWrites d) => d.State switch
    {
        DayState.Off => "off",
        DayState.Unknown => "?",
        _ => $"{d.Units * HealthLog.BytesPerDataUnit / 1e9:0.0} GB{(d.IncludesGap ? "*" : "")}",
    };
}
