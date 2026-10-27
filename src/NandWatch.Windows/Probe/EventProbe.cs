using System.Diagnostics.Eventing.Reader;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace NandWatch.Windows.Probe;

/// <summary>
/// Two questions for the System log: can a standard user read it, and what do
/// its storage-related events look like on this machine.
/// </summary>
internal static partial class EventProbe
{
    /// <summary>
    /// Power-Troubleshooter event 1 is written at each resume or Fast Startup
    /// boot. Its HiberWriteDuration exposed the slow hibernation writes; the
    /// probe records every field's NAME so the app reads the right ones.
    /// </summary>
    public static JsonNode Hibernation(int newest = 15) => Try(() =>
    {
        var rows = new JsonArray();
        var query = new EventLogQuery("System", PathType.LogName,
            "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and (EventID=1)]]") { ReverseDirection = true };
        using var reader = new EventLogReader(query);
        for (var i = 0; i < newest && reader.ReadEvent() is { } e; i++)
        {
            using (e)
            {
                var data = new JsonObject();
                foreach (var d in XDocument.Parse(e.ToXml()).Descendants().Where(x => x.Name.LocalName == "Data"))
                    data[(string?)d.Attribute("Name") ?? $"#{data.Count}"] = d.Value;
                rows.Add(new JsonObject { ["time"] = e.TimeCreated?.ToString("yyyy-MM-dd HH:mm:ss"), ["data"] = data });
            }
        }
        return rows;
    });

    private static readonly string[] StorageProviders =
        ["disk", "stornvme", "storahci", "iaStorVD", "iaStorAC", "Ntfs", "Microsoft-Windows-Ntfs", "volmgr", "partmgr", "Microsoft-Windows-StorPort"];

    /// <summary>
    /// Counts by provider, event ID, level and the disk named in the event
    /// (\Device\Harddisk0 is PhysicalDrive0), over the last 90 days. Counts
    /// only: messages can hold paths.
    /// </summary>
    public static JsonNode StorageCounts(int days = 90) => Try(() =>
    {
        var providers = string.Join(" or ", StorageProviders.Select(p => $"@Name='{p}'"));
        var ms = (long)TimeSpan.FromDays(days).TotalMilliseconds;
        var query = new EventLogQuery("System", PathType.LogName,
            $"*[System[Provider[{providers}] and TimeCreated[timediff(@SystemTime) <= {ms}]]]");
        var groups = new Dictionary<(string, int, string, string), (int Count, DateTime? Oldest, DateTime? Newest)>();
        using var reader = new EventLogReader(query);
        for (var i = 0; i < 50_000 && reader.ReadEvent() is { } e; i++)
        {
            using (e)
            {
                var key = (e.ProviderName ?? "?", e.Id, e.LevelDisplayNameSafe(), DiskOf(e));
                groups.TryGetValue(key, out var g);
                var newest = g.Newest is null || e.TimeCreated > g.Newest ? e.TimeCreated : g.Newest;
                var oldest = g.Oldest is null || e.TimeCreated < g.Oldest ? e.TimeCreated : g.Oldest;
                groups[key] = (g.Count + 1, oldest, newest);
            }
        }
        var rows = new JsonArray();
        foreach (var ((provider, id, level, disk), g) in groups.OrderBy(k => k.Key.Item1).ThenBy(k => k.Key.Item2).ThenBy(k => k.Key.Item4))
            rows.Add(new JsonObject
            {
                ["provider"] = provider, ["id"] = id, ["level"] = level, ["disk"] = disk, ["count"] = g.Count,
                ["oldest"] = g.Oldest?.ToString("yyyy-MM-dd HH:mm"), ["newest"] = g.Newest?.ToString("yyyy-MM-dd HH:mm"),
            });
        return rows;
    });

    /// <summary>The first "HarddiskN" or "Disk N" in the event's own fields; "" when it names none.</summary>
    private static string DiskOf(EventRecord e)
    {
        foreach (var p in e.Properties)
            if (p.Value is string text && DiskName().Match(text) is { Success: true } m)
                return "disk " + (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        return "";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"Harddisk(\d+)|\bDisk (\d+)")]
    private static partial System.Text.RegularExpressions.Regex DiskName();

    private static string LevelDisplayNameSafe(this EventRecord e)
    {
        try { return e.LevelDisplayName ?? $"level {e.Level}"; }
        catch (EventLogException) { return $"level {e.Level}"; }
    }

    private static JsonNode Try(Func<JsonNode> f)
    {
        try { return f(); }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            return new JsonObject { ["error"] = ex.GetType().Name + ": " + ex.Message.Trim() };
        }
    }
}
