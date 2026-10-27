using System.Text.Json;

namespace NandWatch.Core.Nvme;

/// <summary>One drive in the RST reader's report: raw, as the elevated helper wrote it.</summary>
public sealed record RstDrive(int Disk, byte[] Descriptor, bool Ok, int Error, string? Detail, uint SrbReturnCode, uint[] Completion, byte[]? Page)
{
    public DeviceDescriptor? Device => DeviceDescriptor.Parse(Descriptor);

    /// <summary>
    /// The completion entry's four dwords as RST copied them back, for the
    /// record only. RST does not fill them as the NVMe specification lays a
    /// completion out: on the owner's PC (2026-10-24) the Micron's valid page
    /// came back with DW3 reading as status 0x80, the Samsung's with 0. So
    /// nothing is decided on them; <see cref="RstReport.CrossCheck"/> is the check.
    /// </summary>
    public string CompletionText => string.Join(" ", Completion.Select(c => c.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)));

    public HealthLog? Health => Ok && Page is { Length: >= HealthLog.Size } p ? HealthLog.TryParse(p) : null;
}

/// <summary>
/// <c>rst-health.json</c>, written by NandWatchRstReader (elevated, as SYSTEM)
/// and read here, unelevated: the helper decodes nothing, so all parsing and
/// every judgement of the page happen in the collector.
/// </summary>
public sealed record RstReport(DateTimeOffset Started, DateTimeOffset Finished, IReadOnlyList<RstDrive> Drives)
{
    /// <summary>Null for a file that is not a version-1 report.</summary>
    public static RstReport? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("version").GetInt32() != 1) return null;
            var drives = root.GetProperty("drives").EnumerateArray().Select(d => new RstDrive(
                d.GetProperty("disk").GetInt32(),
                Convert.FromBase64String(d.GetProperty("descriptor").GetString() ?? ""),
                d.GetProperty("ok").GetBoolean(),
                d.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0,
                d.TryGetProperty("detail", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
                d.TryGetProperty("srbReturnCode", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetUInt32() : 0,
                d.TryGetProperty("completion", out var q) && q.ValueKind == JsonValueKind.Array ? q.EnumerateArray().Select(x => x.GetUInt32()).ToArray() : [],
                d.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.String ? Convert.FromBase64String(p.GetString()!) : null)).ToList();
            return new RstReport(DateTimeOffset.Parse(root.GetProperty("startedUtc").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(root.GetProperty("finishedUtc").GetString()!, System.Globalization.CultureInfo.InvariantCulture), drives);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the reader's page for a drive this app ALSO reads itself agrees
    /// with that drive's own health log, read within two hours of it: the
    /// proof that the pass-through returns real health logs, on every report.
    /// Null when they agree; else what differs. Counters may move between the
    /// two readings, so each has a small allowance.
    /// </summary>
    public static string? CrossCheck(HealthLog rst, HealthLog own)
    {
        static UInt128 Diff(UInt128 a, UInt128 b) => a > b ? a - b : b - a;
        var differs = new List<string>();
        if (rst.AvailableSpare != own.AvailableSpare) differs.Add($"spare {rst.AvailableSpare}% against {own.AvailableSpare}%");
        if (Math.Abs(rst.PercentageUsed - own.PercentageUsed) > 1) differs.Add($"used {rst.PercentageUsed}% against {own.PercentageUsed}%");
        if (Diff(rst.PowerOnHours, own.PowerOnHours) > 3) differs.Add($"power-on hours {rst.PowerOnHours} against {own.PowerOnHours}");
        if (Diff(rst.PowerCycles, own.PowerCycles) > 1) differs.Add($"power cycles {rst.PowerCycles} against {own.PowerCycles}");
        if (Diff(rst.UnsafeShutdowns, own.UnsafeShutdowns) > 1) differs.Add($"unsafe shutdowns {rst.UnsafeShutdowns} against {own.UnsafeShutdowns}");
        if (rst.MediaErrors != own.MediaErrors) differs.Add($"media errors {rst.MediaErrors} against {own.MediaErrors}");
        // Two hours of writes are far under 1% of a drive's lifetime total.
        if (Diff(rst.DataUnitsWritten, own.DataUnitsWritten) * 100 > UInt128.Max(own.DataUnitsWritten, 1)) differs.Add($"data units written {rst.DataUnitsWritten} against {own.DataUnitsWritten}");
        return differs.Count == 0 ? null : string.Join("; ", differs);
    }

    /// <summary>
    /// Whether a page looks like a health log rather than zeros or another
    /// structure: a miniport that "succeeds" without doing the command leaves
    /// the buffer as it was sent, all zero. Null when it passes; else why not.
    /// </summary>
    public static string? Implausible(RstDrive d)
    {
        if (!d.Ok) return d.Detail ?? $"error {d.Error}";
        if (d.Health is not { } h) return "no page came back";
        if (h.PowerOnHours == 0 && h.DataUnitsWritten == 0 && h.PowerCycles == 0) return "the page is empty";
        if (h.AvailableSpare > 100 || h.AvailableSpareThreshold > 100) return "its spare figures are out of range";
        if (h.CompositeTemperatureK != 0 && h.CompositeTemperatureK is < 223 or > 400) return "its temperature is out of range";
        return null;
    }
}
