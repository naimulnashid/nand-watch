using System.Text.Json;
using System.Text.Json.Nodes;
using NandWatch.Core.Nvme;
using NandWatch.Windows.Device;

namespace NandWatch.Windows.Probe;

public sealed record ProbeOptions(string OutDir, bool VendorLogs);

/// <summary>
/// Phase 0: answers, on the real machine, the questions the design waits on.
/// Can a standard user read NVMe health? At which scope? Is the controller on
/// Windows' own NVMe driver or behind Intel RST? Which volumes sit on which
/// drive? Can the System log be read? It only reads.
/// </summary>
public static class ProbeRunner
{
    public static int Run(ProbeOptions options, TextWriter output)
    {
        Directory.CreateDirectory(options.OutDir);
        var elevated = MachineInfo.Elevated();
        var report = new JsonObject
        {
            ["tool"] = "nandwatch probe",
            ["version"] = typeof(ProbeRunner).Assembly.GetName().Version?.ToString(3),
            ["time"] = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            ["windows"] = MachineInfo.Windows(),
            ["elevated"] = elevated,
            ["vendorLogs"] = options.VendorLogs,
        };

        var drives = new List<DriveReport>();
        foreach (var n in Enumerable.Range(0, NvmeReader.MaxDrives).Where(NvmeReader.Exists))
            drives.Add(DriveProbe.Run(n, options.OutDir, options.VendorLogs));
        report["drives"] = new JsonArray(drives.Select(d => (JsonNode)d.Json).ToArray());

        var volumes = Volumes.List();
        report["volumes"] = new JsonArray(volumes.Select(v => (JsonNode)new JsonObject
        {
            ["volume"] = v.Letter, ["format"] = v.Format, ["totalBytes"] = v.TotalBytes, ["freeBytes"] = v.FreeBytes,
            ["target"] = v.Target, ["diskVolume"] = v.IsDiskVolume,
            ["disks"] = new JsonArray(v.Disks.Select(d => (JsonNode)d).ToArray()), ["error"] = v.Error,
        }).ToArray());

        report["wmi"] = WmiProbe.Run();
        report["hibernation"] = EventProbe.Hibernation();
        report["storageEvents"] = EventProbe.StorageCounts();

        var file = Path.Combine(options.OutDir, "report.json");
        File.WriteAllText(file, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Summarise(output, report, drives, volumes, elevated, file);
        return 0;
    }

    private static void Summarise(TextWriter o, JsonObject report, List<DriveReport> drives, IReadOnlyList<VolumeInfo> volumes, bool elevated, string file)
    {
        o.WriteLine($"NAND Watch probe {report["version"]}, {report["time"]}");
        o.WriteLine($"{report["windows"]}, {(elevated ? "ELEVATED" : "standard user (not elevated)")}");
        o.WriteLine();
        foreach (var d in drives)
        {
            var j = d.Json;
            o.WriteLine($"PhysicalDrive{d.Number}: {d.Device?.Product ?? "?"}, firmware {d.Device?.Revision ?? "?"}, id {MachineInfo.Mask(d.Device?.Serial ?? "")}, on {d.Device?.BusName ?? "?"} (adapter bus {j["adapterBus"]})");
            o.WriteLine($"  open, no access: {j["openNoAccess"]}; open, GENERIC_READ: {j["openGenericRead"]}");
            foreach (var (key, value) in d.Reads) o.WriteLine($"  {key,-30} {value}");
            if (d.IdentifyNote is not null) o.WriteLine($"  {d.IdentifyNote}");
            if (d.Temperature is { } t)
                o.WriteLine($"  temperature: {t.Describe()}");
            if (d.ErrorLog is { } errors)
                o.WriteLine(errors.Count == 0 ? "  error log: empty" : $"  error log: {errors.Count} entries; newest {errors.MaxBy(e => e.ErrorCount)!.Describe()}; " +
                    string.Join(", ", errors.GroupBy(e => e.Meaning).Select(g => $"{g.Count()} x {g.Key}")));
            if (d.Identify is { } id)
                o.WriteLine($"  {id.Model}, firmware {id.Firmware}, NVMe {id.Version}, serial {MachineInfo.Mask(id.SerialNumber)}, " +
                            $"HMB {id.HostMemoryBufferPreferred * 4 / 1024.0:0.#} MiB preferred, temp limits {HealthLog.Celsius(id.WarningTemperatureK)}/{HealthLog.Celsius(id.CriticalTemperatureK)} C");
            if (d.Health is { } h)
                o.WriteLine($"  {HealthLog.Celsius(h.CompositeTemperatureK)} C, spare {h.AvailableSpare}% (threshold {h.AvailableSpareThreshold}%), used {h.PercentageUsed}%, " +
                            $"written {HealthLog.Terabytes(h.DataUnitsWritten):0.00} TB, {h.PowerOnHours} h on, {h.UnsafeShutdowns} unsafe shutdowns, " +
                            $"{h.MediaErrors} media errors, warning 0x{(byte)h.CriticalWarning:x2}");
            o.WriteLine();
        }
        foreach (var v in volumes)
            o.WriteLine($"{v.Letter} {v.Format}: {(v.TotalBytes > 0 ? 100.0 * v.FreeBytes / v.TotalBytes : 0):0.0}% free of {v.TotalBytes / 1e9:0.0} GB, " +
                        (v.IsDiskVolume ? $"on disk {string.Join("+", v.Disks)}" : $"NOT a disk volume ({v.Target ?? "no target"})") + (v.Error is null ? "" : $" ({v.Error})"));
        o.WriteLine();
        o.WriteLine($"Reliability counters (WMI): {Outcome(report["wmi"]?["physicalDisks"])}");
        o.WriteLine($"System log, hibernation events: {Outcome(report["hibernation"])}");
        o.WriteLine($"System log, storage events (90 days): {Outcome(report["storageEvents"])}");
        if (report["storageEvents"] is JsonArray events)
            foreach (var e in events.Where(e => e?["level"]?.ToString() != "Information"))
                o.WriteLine($"  {e!["provider"]} {e["id"]} {e["level"]}, {(e["disk"]?.ToString() is { Length: > 0 } disk ? disk : "no disk named")}: {e["count"]} from {e["oldest"]} to {e["newest"]}");
        o.WriteLine();
        o.WriteLine($"Full report and raw pages: {Path.GetDirectoryName(file)}");
        o.WriteLine("Serials are masked in this output and in report.json. The .bin files are raw and hold them: keep them out of git and out of public places.");
        if (!elevated) o.WriteLine("Next: run the same command from an elevated terminal, to see what changes.");
    }

    private static string Outcome(JsonNode? node) => node switch
    {
        JsonObject o when o["error"] is { } e => "FAILED, " + e,
        JsonArray a when a.Count > 0 && a[0]?["reliability"] is JsonObject r && r["error"] is { } e => "FAILED, " + e,
        JsonArray a => $"ok, {a.Count} entries",
        _ => "?",
    };
}
