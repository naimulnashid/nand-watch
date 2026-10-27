using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace NandWatch.Windows.Probe;

/// <summary>
/// What Windows' storage WMI says about the disks: MSFT_PhysicalDisk, the
/// reliability counters behind Get-StorageReliabilityCounter (which hold no
/// bytes written, so they are a cross-check only), and the storage
/// controllers with their drivers (stornvme, or Intel RST / VMD).
/// </summary>
internal static class WmiProbe
{
    public static JsonObject Run()
    {
        var result = new JsonObject();
        result["physicalDisks"] = Try(() =>
        {
            var disks = new JsonArray();
            using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk");
            foreach (var o in searcher.Get().Cast<ManagementObject>())
            {
                using (o)
                {
                    var disk = Pick(o, "DeviceId", "FriendlyName", "BusType", "MediaType", "HealthStatus", "OperationalStatus", "Size", "FirmwareVersion");
                    if (o["SerialNumber"] is string serial) disk["SerialNumber"] = MachineInfo.Mask(serial.Trim());
                    disk["reliability"] = Try(() =>
                    {
                        var counters = new JsonArray();
                        foreach (var r in o.GetRelated("MSFT_StorageReliabilityCounter").Cast<ManagementObject>())
                            using (r) counters.Add(All(r));
                        return counters;
                    });
                    disks.Add(disk);
                }
            }
            return disks;
        });
        result["controllers"] = Try(() => Query(@"root\cimv2", "SELECT Name, DriverName, Manufacturer, PNPDeviceID FROM Win32_SCSIController"));
        result["diskDrives"] = Try(() => Query(@"root\cimv2", "SELECT Index, Model, InterfaceType, FirmwareRevision, Size FROM Win32_DiskDrive"));
        return result;
    }

    private static JsonArray Query(string scope, string wql)
    {
        var rows = new JsonArray();
        using var searcher = new ManagementObjectSearcher(scope, wql);
        foreach (var o in searcher.Get().Cast<ManagementObject>()) using (o) rows.Add(All(o));
        return rows;
    }

    private static JsonObject All(ManagementBaseObject o)
    {
        var row = new JsonObject();
        foreach (var p in o.Properties)
        {
            if (p.Name.Contains("Serial", StringComparison.OrdinalIgnoreCase)) continue;
            row[p.Name] = Value(p.Value);
        }
        return row;
    }

    private static JsonObject Pick(ManagementBaseObject o, params string[] names)
    {
        var row = new JsonObject();
        foreach (var n in names) row[n] = Value(o[n]);
        return row;
    }

    private static JsonNode? Value(object? v) => v switch
    {
        null => null,
        Array a => new JsonArray(a.Cast<object?>().Select(Value).ToArray()),
        _ => JsonValue.Create(v.ToString()),
    };

    /// <summary>Access denied is an answer here, not a failure: it is what the probe is for.</summary>
    private static JsonNode Try(Func<JsonNode> f)
    {
        try { return f(); }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            return new JsonObject { ["error"] = ex.GetType().Name + ": " + ex.Message.Trim() };
        }
    }
}
