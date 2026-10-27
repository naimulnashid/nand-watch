using System.Text.Json;

namespace NandWatch.Core.Nvme;

/// <summary>
/// The parts of <c>smartctl -a -j</c> output that the health log also holds.
/// smartmontools is the reference these readings are checked against, as
/// SrumECmd was for Data Usage Native; it is never shipped or required.
/// </summary>
/// <remarks>
/// Key names are smartctl 7's JSON (<c>nvme_smart_health_information_log</c>).
/// A key it does not print is reported as absent, not as a difference, so a
/// smartctl version that names a field differently shows up as a gap to look
/// at rather than a false mismatch.
/// </remarks>
public sealed record SmartctlReading(string Model, string Serial, string Firmware, IReadOnlyDictionary<string, UInt128> Health, IReadOnlyList<int> SensorsC)
{
    public static SmartctlReading Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var health = new Dictionary<string, UInt128>();
        var sensors = new List<int>();
        if (root.TryGetProperty("nvme_smart_health_information_log", out var log) && log.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in log.EnumerateObject())
            {
                if (p.Name == "temperature_sensors" && p.Value.ValueKind == JsonValueKind.Array)
                    sensors.AddRange(p.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetInt32()));
                else if (p.Value.ValueKind == JsonValueKind.Number && UInt128.TryParse(p.Value.GetRawText(), out var n))
                    health[p.Name] = n;
            }
        }
        return new SmartctlReading(Text(root, "model_name"), Text(root, "serial_number"), Text(root, "firmware_version"), health, sensors);
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
}

public enum Agreement { Match, Differ, Absent }

public sealed record ComparedField(string Field, string Ours, string Theirs, Agreement Agreement);

/// <summary>
/// Checks one smartctl reading against two of ours, taken just before and just
/// after it. A drive's counters move while it is read (host commands, busy
/// time, temperature), so an exact comparison with one reading would fail on
/// a healthy, correct parser. Bracketing makes it exact: smartctl's value must
/// lie between our two.
/// </summary>
public static class HealthCompare
{
    public static IReadOnlyList<ComparedField> Compare(HealthLog before, HealthLog after, IdentifyController? identify, SmartctlReading theirs)
    {
        var rows = new List<ComparedField>();
        if (identify is not null)
        {
            Text("model_name", identify.Model, theirs.Model);
            Text("serial_number", identify.SerialNumber, theirs.Serial);
            Text("firmware_version", identify.Firmware, theirs.Firmware);
        }

        Counter("critical_warning", (byte)before.CriticalWarning, (byte)after.CriticalWarning);
        Counter("temperature", Celsius(before.CompositeTemperatureK), Celsius(after.CompositeTemperatureK));
        Counter("available_spare", before.AvailableSpare, after.AvailableSpare);
        Counter("available_spare_threshold", before.AvailableSpareThreshold, after.AvailableSpareThreshold);
        Counter("percentage_used", before.PercentageUsed, after.PercentageUsed);
        Counter("data_units_read", before.DataUnitsRead, after.DataUnitsRead);
        Counter("data_units_written", before.DataUnitsWritten, after.DataUnitsWritten);
        Counter("host_reads", before.HostReadCommands, after.HostReadCommands);
        Counter("host_writes", before.HostWriteCommands, after.HostWriteCommands);
        Counter("controller_busy_time", before.ControllerBusyMinutes, after.ControllerBusyMinutes);
        Counter("power_cycles", before.PowerCycles, after.PowerCycles);
        Counter("power_on_hours", before.PowerOnHours, after.PowerOnHours);
        Counter("unsafe_shutdowns", before.UnsafeShutdowns, after.UnsafeShutdowns);
        Counter("media_errors", before.MediaErrors, after.MediaErrors);
        Counter("num_err_log_entries", before.ErrorLogEntries, after.ErrorLogEntries);
        Counter("warning_temp_time", before.WarningTemperatureMinutes, after.WarningTemperatureMinutes);
        Counter("critical_comp_time", before.CriticalTemperatureMinutes, after.CriticalTemperatureMinutes);

        // Sensors: smartctl lists only the ones a drive has, in order. Compared
        // as a list, each sensor allowed to move between our two readings.
        var lo = before.TemperatureSensorsK.Zip(after.TemperatureSensorsK).Where(p => p.First != 0 || p.Second != 0)
            .Select(p => (Min: Math.Min(SensorC(p.First), SensorC(p.Second)), Max: Math.Max(SensorC(p.First), SensorC(p.Second)))).ToList();
        var ours = string.Join(", ", lo.Select(s => s.Min == s.Max ? $"{s.Min}" : $"{s.Min}..{s.Max}"));
        var agree = lo.Count == theirs.SensorsC.Count && lo.Zip(theirs.SensorsC).All(p => p.Second >= p.First.Min && p.Second <= p.First.Max);
        rows.Add(new ComparedField("temperature_sensors", ours, string.Join(", ", theirs.SensorsC), agree ? Agreement.Match : Agreement.Differ));
        return rows;

        void Text(string key, string mine, string theirsText) =>
            rows.Add(new ComparedField(key, mine, theirsText, theirsText.Length == 0 ? Agreement.Absent
                : string.Equals(mine, theirsText, StringComparison.Ordinal) ? Agreement.Match : Agreement.Differ));

        void Counter(string key, UInt128 a, UInt128 b)
        {
            var (min, max) = a <= b ? (a, b) : (b, a);
            var mine = min == max ? $"{min}" : $"{min}..{max}";
            if (!theirs.Health.TryGetValue(key, out var v)) { rows.Add(new ComparedField(key, mine, "", Agreement.Absent)); return; }
            rows.Add(new ComparedField(key, mine, v.ToString(), v >= min && v <= max ? Agreement.Match : Agreement.Differ));
        }
    }

    /// <summary>smartctl's Celsius: kelvin minus 273, and a missing reading stays 0 so it cannot match a real one by accident.</summary>
    private static UInt128 Celsius(ushort kelvin) => kelvin > 273 ? (UInt128)(kelvin - 273) : 0;

    private static int SensorC(ushort kelvin) => kelvin - 273;
}
