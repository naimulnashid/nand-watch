using System.Diagnostics;
using System.Text.Json.Nodes;
using NandWatch.Core.Nvme;
using NandWatch.Windows.Device;

namespace NandWatch.Windows.Probe;

/// <summary>Everything the probe learns about one physical drive.</summary>
internal sealed class DriveReport
{
    public required int Number { get; init; }
    public JsonObject Json { get; } = new();
    public DeviceDescriptor? Device { get; set; }
    public int? AdapterBus { get; set; }
    public IdentifyController? Identify { get; set; }
    public HealthLog? Health { get; set; }

    /// <summary>Set when the Identify Controller reply turned out to be something else, and what.</summary>
    public string? IdentifyNote { get; set; }

    public TemperatureDescriptor? Temperature { get; set; }
    public byte[]? TemperatureRaw { get; set; }
    public IReadOnlyList<ErrorLogEntry>? ErrorLog { get; set; }

    /// <summary>"scope/read" -> ok or the error, for the summary table.</summary>
    public Dictionary<string, string> Reads { get; } = [];
}

/// <summary>
/// Reads one drive every way the app might: both open modes, both query
/// scopes, every standard log, and (only when asked) the vendor logs. Each raw
/// reply is saved beside the report so it can be parsed again later.
/// </summary>
internal static class DriveProbe
{
    public static DriveReport Run(int number, string outDir, bool vendorLogs)
    {
        var report = new DriveReport { Number = number };
        var j = report.Json;
        j["number"] = number;
        j["path"] = NvmeReader.PathOf(number);

        using var reader = NvmeReader.Open(number, Native.NoAccess, out var noAccessError);
        j["openNoAccess"] = reader is null ? Err(noAccessError) : "ok";
        using (var withRead = NvmeReader.Open(number, Native.GenericRead, out var readError))
        {
            j["openGenericRead"] = withRead is null ? Err(readError) : "ok";
            if (withRead is not null)
            {
                // Does GENERIC_READ change what the driver answers? Only the two reads that matter.
                var extra = new JsonObject();
                foreach (var scope in new[] { StorageProperty.AdapterProtocolSpecific, StorageProperty.DeviceProtocolSpecific })
                    foreach (var read in new[] { NvmeRead.IdentifyController, NvmeRead.HealthLog })
                        extra[$"{ScopeName(scope)}/{read.Name}"] = Summary(Timed(() => withRead.Read(scope, read)));
                j["withGenericRead"] = extra;
            }
        }
        if (reader is null) return report;

        j["deviceNumber"] = reader.DeviceNumber();

        var device = Save(reader.Descriptor(StorageProperty.Device), outDir, $"disk{number}-prop00-device.bin");
        if (device.Ok && DeviceDescriptor.Parse(device.Data) is { } d)
        {
            report.Device = d;
            j["descriptor"] = new JsonObject
            {
                ["bus"] = d.BusName, ["busType"] = d.BusType, ["removable"] = d.Removable,
                ["vendor"] = d.Vendor, ["product"] = d.Product, ["revision"] = d.Revision, ["serial"] = MachineInfo.Mask(d.Serial),
            };
        }
        else j["descriptor"] = device.Detail;

        var adapter = Save(reader.Descriptor(StorageProperty.Adapter), outDir, $"disk{number}-prop01-adapter.bin");
        report.AdapterBus = adapter.Ok ? AdapterDescriptor.BusType(adapter.Data) : null;
        j["adapterBus"] = report.AdapterBus is int ab ? StorageBus.Name(ab) : adapter.Detail;

        // Descriptors Windows builds from the drive's own data. Saved raw:
        // their layouts are checked against these bytes before the app parses them.
        var props = new JsonObject();
        foreach (var p in new[] { StorageProperty.DeviceTemperature, StorageProperty.DeviceUnsafeShutdownCount, StorageProperty.DeviceEndurance })
        {
            var r = Save(reader.Descriptor(p), outDir, $"disk{number}-prop{(int)p:00}-{p}.bin");
            props[$"{(int)p} {p}"] = r.Ok ? $"ok, {r.Data.Length} bytes" : r.Detail;
            if (p == StorageProperty.DeviceTemperature && r.Ok)
            {
                report.TemperatureRaw = r.Data;
                report.Temperature = TemperatureDescriptor.Parse(r.Data);
            }
        }
        if (report.Temperature is { } t)
            j["temperature"] = new JsonObject
            {
                ["temperatureC"] = t.TemperatureC, ["warningC"] = t.EffectiveWarningC, ["criticalC"] = t.CriticalC,
                ["raw"] = Convert.ToHexString(report.TemperatureRaw!),
            };
        j["properties"] = props;

        var nvme = new JsonObject();
        foreach (var scope in new[] { StorageProperty.AdapterProtocolSpecific, StorageProperty.DeviceProtocolSpecific })
        {
            foreach (var read in new[] { NvmeRead.IdentifyController, NvmeRead.HealthLog })
            {
                var key = $"{ScopeName(scope)}/{read.Name}";
                var r = Save(Timed(() => reader.Read(scope, read)), outDir, $"disk{number}-{ScopeName(scope)}-{read.Name}.bin");
                nvme[key] = Summary(r);
                report.Reads[key] = r.Ok ? "ok" : r.Detail;
                if (!r.Ok) continue;
                if (read == NvmeRead.IdentifyController)
                {
                    if (report.Identify is not null) continue;
                    var parsed = IdentifyController.TryParse(r.Data);
                    if (parsed is { IsPlausible: true }) report.Identify = parsed;
                    else if (IdentifyNamespace.SizeBytes(r.Data) is { } size)
                        report.IdentifyNote = $"{key} returned Identify NAMESPACE data ({size:N0} bytes), not the controller's";
                    else report.IdentifyNote = $"{key} returned data that is not Identify Controller";
                }
                else report.Health ??= HealthLog.TryParse(r.Data);
            }
        }

        // The other logs at device scope, then adapter scope where device fails.
        var logs = vendorLogs ? NvmeRead.StandardLogs.Concat(Enumerable.Range(0xC0, 0x40).Select(i => NvmeRead.VendorLog((byte)i))) : NvmeRead.StandardLogs;
        foreach (var read in logs)
        {
            var r = Timed(() => reader.Read(StorageProperty.DeviceProtocolSpecific, read));
            var scope = "device";
            if (!r.Ok)
            {
                var a = Timed(() => reader.Read(StorageProperty.AdapterProtocolSpecific, read));
                if (a.Ok) (r, scope) = (a, "adapter");
            }
            if (r.Ok) Save(r, outDir, $"disk{number}-{scope}-{read.Name}.bin");
            if (r.Ok && read.RequestValue == ErrorLogEntry.LogId) report.ErrorLog = ErrorLogEntry.Parse(r.Data);
            // A vendor page every byte of which is zero is as good as absent; say which.
            var empty = r.Ok && r.Data.All(b => b == 0);
            if (r.Ok || read.RequestValue < 0xC0) nvme[$"{scope}/{read.Name}"] = Summary(r) + (empty ? ", all zero" : "");
        }
        j["nvme"] = nvme;
        if (report.IdentifyNote is not null) j["identifyNote"] = report.IdentifyNote;
        if (report.ErrorLog is { } errors)
            j["errorLog"] = new JsonArray(errors.Select(e => (JsonNode)e.Describe()).ToArray());

        if (report.Identify is { } id)
        {
            j["identify"] = new JsonObject
            {
                ["model"] = id.Model, ["serial"] = MachineInfo.Mask(id.SerialNumber), ["firmware"] = id.Firmware, ["nvmeVersion"] = id.Version,
                ["vendorId"] = $"0x{id.VendorId:x4}", ["capacityBytes"] = id.TotalCapacityBytes.ToString(),
                ["warningTempC"] = HealthLog.Celsius(id.WarningTemperatureK), ["criticalTempC"] = HealthLog.Celsius(id.CriticalTemperatureK),
                ["hmbPreferredMiB"] = id.HostMemoryBufferPreferred * 4 / 1024.0, ["hmbMinimumMiB"] = id.HostMemoryBufferMinimum * 4 / 1024.0,
                ["healthLogPerNamespace"] = (id.LogPageAttributes & 1) != 0, ["powerStates"] = id.PowerStateCount, ["autonomousPowerStates"] = id.AutonomousPowerStates,
            };
        }
        if (report.Health is { } h)
        {
            var fields = new JsonObject();
            foreach (var (name, value) in h.Fields()) fields[name] = value;
            j["health"] = fields;
        }
        return report;
    }

    public static string ScopeName(StorageProperty scope) => scope == StorageProperty.AdapterProtocolSpecific ? "adapter" : "device";

    private static ReadResult Timed(Func<ReadResult> read)
    {
        var sw = Stopwatch.StartNew();
        var r = read();
        sw.Stop();
        return r with { Detail = r.Ok ? $"{sw.Elapsed.TotalMilliseconds:0.0} ms" : $"{r.Detail} ({sw.Elapsed.TotalMilliseconds:0.0} ms)" };
    }

    private static string Summary(ReadResult r) => r.Ok ? $"ok, {r.Data.Length} bytes, {r.Detail}" : r.Detail;

    private static string Err(int error) => ReadResult.Failed(error).Detail;

    private static ReadResult Save(ReadResult r, string outDir, string name)
    {
        if (r.Ok) File.WriteAllBytes(Path.Combine(outDir, name), r.Data);
        return r;
    }
}
