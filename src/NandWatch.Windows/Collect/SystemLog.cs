using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Text.RegularExpressions;
using NandWatch.Core.Data;
using NandWatch.Core.View;

namespace NandWatch.Windows.Collect;

/// <summary>
/// Reads, from Windows' System log, the events the history keeps: storage
/// warnings and errors (bad blocks, controller errors, resets, file-system
/// corruption) and Windows' own record of an unexpected shutdown. A standard
/// user can read the System log (Phase 0). Three simple queries rather than
/// one compound one: the log's XPath subset is easy to get subtly wrong.
/// </summary>
public static partial class SystemLog
{
    /// <summary>Every such event at or after <paramref name="since"/> (all the log holds when null), oldest first.</summary>
    public static List<SystemEvent> Since(DateTimeOffset? since, int max = 50_000)
    {
        var time = since is { } s ? $" and TimeCreated[@SystemTime>='{s.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)}']" : "";
        var storage = string.Join(" or ", SystemEvents.StorageProviders.Select(p => $"@Name='{p}'"));
        var events = new List<SystemEvent>();
        foreach (var filter in new[]
        {
            $"Provider[{storage}] and (Level=1 or Level=2 or Level=3)",
            $"Provider[@Name='{SystemEvents.KernelPower}'] and EventID=41",
            $"Provider[@Name='{SystemEvents.EventLog}'] and EventID=6008",
        })
        {
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, $"*[System[{filter}{time}]]"));
            while (events.Count < max && reader.ReadEvent() is { } e)
                using (e)
                    if (e.TimeCreated is { } at && e.RecordId is { } record)
                        events.Add(new SystemEvent(new DateTimeOffset(at.ToUniversalTime(), TimeSpan.Zero), e.ProviderName ?? "?", e.Id, e.Level ?? 4, DiskOf(e), record));
        }
        return events.OrderBy(e => e.At).ToList();
    }

    /// <summary>The disk the event's own fields name ("\Device\Harddisk2\DR2", "Disk 2"); null when none.</summary>
    private static int? DiskOf(EventRecord e)
    {
        foreach (var p in e.Properties)
            if (p.Value is string text && DiskName().Match(text) is { Success: true } m)
                return int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, CultureInfo.InvariantCulture);
        return null;
    }

    [GeneratedRegex(@"Harddisk(\d+)|\bDisk (\d+)")]
    private static partial Regex DiskName();
}
