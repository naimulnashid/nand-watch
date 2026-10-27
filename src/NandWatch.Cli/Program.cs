using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NandWatch.Core.Nvme;
using NandWatch.Windows.Device;
using NandWatch.Windows.Probe;

namespace NandWatch.Cli;

/// <summary>
/// <c>nandwatch</c>: the probe, a one-drive read, and the check against
/// smartctl. Everything the app will read from a drive can be read and
/// checked from here first.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length == 0) return Usage();
        var measure = args.Contains("--measure");
        var rest = args[1..].Where(a => a != "--measure").ToArray();
        var clock = Stopwatch.StartNew();
        try
        {
            return args[0] switch
            {
                "probe" => Probe(rest),
                "read" => Read(rest),
                "compare" => Compare(rest),
                "collect" => HistoryCommands.Collect(rest),
                "stats" => HistoryCommands.Stats(rest),
                "where" => HistoryCommands.Where(),
                "alerts" => HistoryCommands.Alerts(rest),
                "notify-test" => HistoryCommands.NotifyTest(),
                "rst-show" => HistoryCommands.RstShow(rest),
                "demo-data" => DemoCommand.Run(rest),
                _ => Usage(),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or JsonException or FormatException or System.ComponentModel.Win32Exception or Microsoft.Data.Sqlite.SqliteException)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 1;
        }
        finally
        {
            if (measure) Measure(clock);
        }
    }

    /// <summary>
    /// What this run cost the machine, measured from inside: wall time from
    /// Main (process start and runtime load come on top), CPU time and peak
    /// memory of the whole process. The background collector's cost will be
    /// of the same kind, so this is how it is measured, not estimated.
    /// </summary>
    private static void Measure(Stopwatch clock)
    {
        using var self = Process.GetCurrentProcess();
        self.Refresh();
        var started = (DateTime.Now - self.StartTime).TotalMilliseconds;
        Console.WriteLine();
        Console.WriteLine($"measure: {started:0} ms since the process started ({clock.Elapsed.TotalMilliseconds:0} ms in Main), " +
                          $"CPU {self.TotalProcessorTime.TotalMilliseconds:0} ms, peak memory {self.PeakWorkingSet64 / 1048576.0:0.0} MB (working set), " +
                          $"{self.PrivateMemorySize64 / 1048576.0:0.0} MB private at the end");
    }

    private static int Usage()
    {
        Console.WriteLine("""
            nandwatch probe [--out <dir>] [--vendor-logs]
                Phase 0. Reads every drive every way the app might, and reports what
                worked: run it once normally and once from an elevated terminal.
                Writes report.json and the raw pages to --out (default: a new folder
                under %TEMP%\nandwatch-probe). --vendor-logs also reads log pages
                C0h-FFh, whose contents are undocumented.
            nandwatch read [--disk N]
                One drive's Identify and health log, field by field (default: every drive).

            nandwatch collect [--backup] [--retry-refused]
                One collector run now, as the hourly task does: every NVMe drive into the
                history. --backup writes the backup even if today's exists;
                --retry-refused asks drives again for what they refused before.
            nandwatch stats [--days N]
                What the history holds: each drive's latest reading, life used, pace,
                written per day for the last N days (default 14), free space, recent runs.
                In the daily line, ? is a day with no reading, off a day the drive was off,
                and * a day whose total includes writes since an earlier gap.
            nandwatch where
                Where the data folder, database, backup and logs are.
            nandwatch alerts [--count N]
                The newest alerts the collector raised (default 20), newest first.
            nandwatch notify-test
                Shows one sample Windows notification, as an alert would.
            nandwatch rst-show [file]
                Decodes the RST reader's report (default: the installed reader's, under
                %ProgramData%\NAND Watch\rst): each drive's health log, field by field,
                and whether the collector would use it. Compare it with HWiNFO64.
            nandwatch demo-data <dir> [--days N]
                An invented history (default 180 days) for screenshots and trying things out.
                Prints the two variables that point the app and the CLI at it.

            Add --measure to any command to print its wall time, CPU time and peak memory.
            nandwatch compare --smartctl <path to smartctl.exe>
                Reads each drive, runs smartctl on it, reads it again, and checks every
                field smartctl prints lies between our two readings. Needs an elevated
                terminal, because smartctl does.
            """);
        return 2;
    }

    internal static bool Flag(string[] args, string name) => args.Contains("--" + name);

    internal static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, "--" + name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Probe(string[] args)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var outDir = Arg(args, "out") ?? Path.Combine(Path.GetTempPath(), "nandwatch-probe", $"{stamp}-{(MachineInfo.Elevated() ? "elevated" : "standard")}");
        return ProbeRunner.Run(new ProbeOptions(outDir, Flag(args, "vendor-logs")), Console.Out);
    }

    /// <summary>Drive numbers that exist, in order.</summary>
    private static IEnumerable<int> Drives(string[] args)
    {
        if (Arg(args, "disk") is { } d) return [int.Parse(d, CultureInfo.InvariantCulture)];
        return Enumerable.Range(0, NvmeReader.MaxDrives).Where(NvmeReader.Exists).ToList();
    }

    /// <summary>One drive as the app would read it.</summary>
    private sealed record DriveReading(DeviceDescriptor? Device, IdentifyController? Identify, HealthLog? Health,
        TemperatureDescriptor? Temperature, IReadOnlyList<ErrorLogEntry>? Errors, string Note);

    /// <summary>
    /// Identity from Windows' device descriptor, which works behind Intel RST
    /// where Identify Controller does not. The health log at device scope
    /// first, then adapter: which one a machine answers is what the probe found.
    /// </summary>
    private static DriveReading ReadDrive(int n)
    {
        using var reader = NvmeReader.Open(n, NvmeReader.NoAccess, out var error);
        if (reader is null) return new(null, null, null, null, null, ReadResult.Failed(error).Detail);
        var device = reader.Descriptor(StorageProperty.Device) is { Ok: true } d ? DeviceDescriptor.Parse(d.Data) : null;
        // A request the collector found refused is not sent again from here either:
        // each attempt adds an entry to the drive's own error log.
        var healthRefused = device is not null && Remembered(device, NandWatch.Windows.Collect.Collector.HealthRequest);
        var temperature = reader.Descriptor(StorageProperty.DeviceTemperature) is { Ok: true } t ? TemperatureDescriptor.Parse(t.Data) : null;
        IdentifyController? id = null;
        HealthLog? health = null;
        IReadOnlyList<ErrorLogEntry>? errors = null;
        var notes = new List<string>();
        foreach (var scope in new[] { StorageProperty.DeviceProtocolSpecific, StorageProperty.AdapterProtocolSpecific })
        {
            if (id is null && reader.Read(scope, NvmeRead.IdentifyController) is { Ok: true } i && IdentifyController.TryParse(i.Data) is { IsPlausible: true } parsed) id = parsed;
            var h = health is null && !healthRefused ? reader.Read(scope, NvmeRead.HealthLog) : null;
            if (h is { Ok: true }) health = HealthLog.TryParse(h.Data);
            else if (h is not null) notes.Add($"{(scope == StorageProperty.DeviceProtocolSpecific ? "device" : "adapter")}: {h.Detail}");
            if (errors is null && reader.Read(scope, NvmeRead.StandardLogs[0]) is { Ok: true } e) errors = ErrorLogEntry.Parse(e.Data);
        }
        if (healthRefused) notes.Add("refused before, as the history remembers; not asked again (nandwatch collect --retry-refused asks)");
        return new(device, id, health, temperature, errors, string.Join("; ", notes));
    }

    private static bool Remembered(DeviceDescriptor device, string request)
    {
        if (NandWatch.Core.AppPaths.DataDir is not { } dir || !File.Exists(NandWatch.Core.AppPaths.DatabasePath(dir))) return false;
        using var conn = NandWatch.Core.Data.HealthDb.OpenRead(NandWatch.Core.AppPaths.DatabasePath(dir));
        return NandWatch.Core.Query.HealthQueries.IsRefused(conn, device.Product, device.Serial, request, device.Revision);
    }

    private static int Read(string[] args)
    {
        foreach (var n in Drives(args))
        {
            var r = ReadDrive(n);
            Console.WriteLine($"PhysicalDrive{n}");
            if (r.Device is { } d)
            {
                Console.WriteLine($"  {"Model",-38} {d.Product}");
                Console.WriteLine($"  {"Firmware",-38} {d.Revision}");
                Console.WriteLine($"  {"Id (EUI-64 / NGUID)",-38} {MachineInfo.Mask(d.Serial)}");
                Console.WriteLine($"  {"Bus",-38} {d.BusName}");
            }
            if (r.Temperature is { } t)
                Console.WriteLine($"  {"Temperature (Windows descriptor)",-38} {t.Describe()}");
            if (r.Errors is { } errors)
                Console.WriteLine($"  {"Error log",-38} {(errors.Count == 0 ? "empty" : $"{errors.Count} entries, newest {errors.MaxBy(e => e.ErrorCount)!.Describe()}")}");
            if (r.Health is null) Console.WriteLine($"  health log not readable: {r.Note}");
            else foreach (var (name, value) in r.Health.Fields()) Console.WriteLine($"  {name,-38} {value}");
            Console.WriteLine();
        }
        return 0;
    }

    private static int Compare(string[] args)
    {
        var exe = Arg(args, "smartctl") ?? throw new ArgumentException("--smartctl <path to smartctl.exe> is required.");
        using var scan = JsonDocument.Parse(Run(exe, "--scan", "-j"));
        var devices = scan.RootElement.TryGetProperty("devices", out var list) ? list.EnumerateArray().ToList() : [];
        if (devices.Count == 0) throw new InvalidOperationException("smartctl --scan found no devices. Is the terminal elevated?");

        var drives = Drives(args).ToList();
        var compared = 0;
        var differences = 0;
        foreach (var device in devices)
        {
            var name = device.GetProperty("name").GetString()!;
            var type = device.TryGetProperty("type", out var t) ? t.GetString() : null;

            var before = drives.ToDictionary(n => n, ReadDrive);
            var theirs = SmartctlReading.Parse(Run(exe, type is null ? ["-a", "-j", name] : ["-a", "-j", "-d", type, name]));
            var after = drives.ToDictionary(n => n, ReadDrive);

            Console.WriteLine($"{name} ({type}): {(theirs.Model.Length > 0 ? theirs.Model : "smartctl read nothing")}");
            if (theirs.Model.Length == 0 || theirs.Health.Count == 0)
            {
                Console.WriteLine("  smartctl returned no health log for this device (run it by hand to see why); skipped");
                Console.WriteLine();
                continue;
            }
            // Matched on the model: Windows' descriptor serial is the EUI-64 /
            // NGUID, never smartctl's serial_number. Two drives of one model are
            // ambiguous and skipped rather than guessed.
            var matches = drives.Where(n => before[n].Device?.Product == theirs.Model).ToList();
            if (matches.Count != 1 || before[matches[0]].Health is not { } b || after[matches[0]].Health is not { } a)
            {
                Console.WriteLine(matches.Count > 1 ? "  more than one drive of this model; skipped" : "  no drive here of that model with a readable health log; skipped");
                Console.WriteLine();
                continue;
            }
            Console.WriteLine($"  is PhysicalDrive{matches[0]}");
            compared++;
            foreach (var row in HealthCompare.Compare(b, a, before[matches[0]].Identify, theirs))
            {
                var ours = row.Field == "serial_number" ? MachineInfo.Mask(row.Ours) : row.Ours;
                var other = row.Field == "serial_number" ? MachineInfo.Mask(row.Theirs) : row.Theirs;
                Console.WriteLine($"  {row.Agreement,-6} {row.Field,-28} ours {ours,-24} smartctl {other}");
                if (row.Agreement == Agreement.Differ) differences++;
            }
            Console.WriteLine();
        }
        if (compared == 0)
        {
            Console.WriteLine("Nothing was compared: smartctl could not read any drive this tool can.");
            return 1;
        }
        Console.WriteLine($"{compared} drive(s) compared, {differences} difference(s).");
        return differences == 0 ? 0 : 1;
    }

    /// <summary>smartctl's exit code is a bit mask of drive conditions, not success or failure: its JSON is read either way.</summary>
    private static string Run(string exe, params string[] args)
    {
        var info = new ProcessStartInfo(exe) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {exe}.");
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return text;
    }
}
