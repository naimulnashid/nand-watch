using Microsoft.Data.Sqlite;
using NandWatch.Core.Data;
using NandWatch.Core.Drives;
using NandWatch.Core.Nvme;

namespace NandWatch.Core.Query;

public sealed record DriveRow(long Id, string Model, string DeviceId, string Firmware, DateTimeOffset LastSeen, string? Label, decimal? RatedTbw, string? RatingSource)
{
    /// <summary>What the app calls the drive: the label set for it, else the model's readable name.</summary>
    public string Title => Label ?? Ratings.DisplayName(Model);
}

/// <summary>
/// A drive as of its newest reading: <c>At</c>, temperature and the error-log
/// number from that reading; the health fields from the newest reading that
/// carried the health log (<c>HealthAt</c>), which for a drive read through
/// the RST reader is its daily one (<c>HealthSource</c> "rst").
/// </summary>
public sealed record LatestReading(
    DateTimeOffset At, short? SensorTemperatureC, short? WarningTemperatureC, long? ErrorLogNewest, bool HasHealth,
    int? CriticalWarning, int? AvailableSpare, int? SpareThreshold, int? PercentageUsed,
    long? PowerOnHours, long? PowerCycles, long? UnsafeShutdowns, long? MediaErrors,
    long? WarningTemperatureMinutes, long? CriticalTemperatureMinutes,
    DateTimeOffset? HealthAt = null, string? HealthSource = null);

public sealed record VolumeRow(string Letter, long TotalBytes, long FreeBytes, DateTimeOffset At);

public sealed record RunRow(long Id, DateTimeOffset Started, string Trigger, string Status, int DrivesRead, string? Backup, long? DurationMs, string? Error);

/// <summary>The history at a glance, for the Sync page. <c>Runs</c> counts the runs that read at least one drive.</summary>
public sealed record HistorySummary(long Readings, long Runs, DateTimeOffset? First, DateTimeOffset? LastOk, DateTimeOffset? LastBackup);

public sealed record AlertRow(DateTimeOffset At, long? DriveId, string Kind, AlertLevel Level, string Title, string Body);

/// <summary>Kept System-log events of one kind on one disk.</summary>
public sealed record EventGroup(int? Disk, string Provider, int Id, int Level, long Count, DateTimeOffset First, DateTimeOffset Last);

/// <summary>
/// One day's unsafe shutdowns: Windows' own record (Kernel-Power 41 or
/// EventLog 6008, whichever counts more) and each drive's counter growth,
/// both filed under the day they were seen, which is the next start.
/// </summary>
public sealed record ShutdownDay(DateOnly Date, int Windows, IReadOnlyDictionary<long, long> Drives);

public sealed record RefusalRow(string Model, string Request, string Firmware, DateTimeOffset First, string Error);

/// <summary>Every read the app and the CLI make of the history.</summary>
public static class HealthQueries
{
    public static IReadOnlyList<DriveRow> Drives(SqliteConnection conn)
    {
        var rows = new List<DriveRow>();
        using var cmd = HealthDb.Command(conn, """
            SELECT d.id, d.model, d.device_id, d.firmware, d.last_seen_utc, s.label, s.rated_tbw
            FROM drives d LEFT JOIN drive_settings s ON s.drive_id = d.id ORDER BY d.id;
            """);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var model = r.GetString(1);
            var builtIn = Ratings.For(model);
            decimal? own = r.IsDBNull(6) ? null : (decimal)r.GetDouble(6);
            rows.Add(new DriveRow(r.GetInt64(0), model, r.GetString(2), r.GetString(3), HealthDb.ParseIso(r.GetString(4)),
                r.IsDBNull(5) ? null : r.GetString(5), own ?? builtIn?.Tbw, own is not null ? "set for this drive" : builtIn?.Source));
        }
        return rows;
    }

    /// <summary>
    /// This collector's own health-log reading of a drive nearest to
    /// <paramref name="at"/>, within <paramref name="within"/>: what the RST
    /// reader's page for the same drive is checked against. Null when there is none.
    /// </summary>
    public static (DateTimeOffset At, HealthLog Health)? OwnHealthNear(SqliteConnection conn, long driveId, DateTimeOffset at, TimeSpan within)
    {
        using var cmd = HealthDb.Command(conn, """
            SELECT utc, health_page FROM readings
            WHERE drive_id = $d AND source IS NULL AND health_page IS NOT NULL AND utc BETWEEN $from AND $to
            ORDER BY abs(julianday(utc) - julianday($at)) LIMIT 1;
            """, ("$d", driveId), ("$from", HealthDb.Iso(at - within)), ("$to", HealthDb.Iso(at + within)), ("$at", HealthDb.Iso(at)));
        using var r = cmd.ExecuteReader();
        if (!r.Read() || HealthLog.TryParse((byte[])r.GetValue(1)) is not { } health) return null;
        return (HealthDb.ParseIso(r.GetString(0)), health);
    }

    /// <summary>
    /// How often the drive's health log is read: once a day when its newest
    /// came from the RST reader, else hourly. What <see cref="WriteHistory.Daily"/> expects.
    /// </summary>
    public static TimeSpan HealthInterval(SqliteConnection conn, long driveId) =>
        HealthDb.Scalar<string?>(conn, "SELECT source FROM readings WHERE drive_id = $d AND health_page IS NOT NULL ORDER BY utc DESC LIMIT 1;", ("$d", driveId)) == DriveReading.RstSource
            ? WriteHistory.DailyInterval
            : WriteHistory.ShortInterval;

    /// <summary>The drive's first reading of any kind, or null before one: before it there is no history, not a gap.</summary>
    public static DateTimeOffset? FirstReading(SqliteConnection conn, long driveId) =>
        HealthDb.Scalar<string?>(conn, "SELECT MIN(utc) FROM readings WHERE drive_id = $d;", ("$d", driveId)) is { } s ? HealthDb.ParseIso(s) : null;

    /// <summary>
    /// Where a chart covering the last <paramref name="days"/> starts: no
    /// earlier than the first reading, so a short history fills the chart
    /// instead of sitting at its right edge. At least an hour, so a first
    /// reading moments ago still has a range to draw.
    /// </summary>
    public static DateTimeOffset WindowStart(DateTimeOffset now, int days, DateTimeOffset? first)
    {
        var start = now.AddDays(-days);
        if (first is { } f && f > start) start = f;
        return start > now.AddHours(-1) ? now.AddHours(-1) : start;
    }

    /// <summary>The drive's id in the history by its identity, or null when it was never read.</summary>
    public static long? DriveId(SqliteConnection conn, string model, string deviceId) =>
        HealthDb.Scalar<long?>(conn, "SELECT id FROM drives WHERE model = $m AND device_id = $d;", ("$m", model), ("$d", deviceId));

    /// <summary>Data units written, reading by reading; only readings that carried the health log.</summary>
    public static IReadOnlyList<CounterPoint> Written(SqliteConnection conn, long driveId) =>
        Counter(conn, driveId, "data_units_written");

    public static IReadOnlyList<CounterPoint> Read(SqliteConnection conn, long driveId) =>
        Counter(conn, driveId, "data_units_read");

    private static IReadOnlyList<CounterPoint> Counter(SqliteConnection conn, long driveId, string column)
    {
        var points = new List<CounterPoint>();
        using var cmd = HealthDb.Command(conn, $"SELECT utc, local_date, {column}, power_on_hours FROM readings WHERE drive_id = $d AND {column} IS NOT NULL ORDER BY utc;", ("$d", driveId));
        using var r = cmd.ExecuteReader();
        while (r.Read())
            points.Add(new CounterPoint(HealthDb.ParseIso(r.GetString(0)), DateOnly.ParseExact(r.GetString(1), "yyyy-MM-dd"), r.GetInt64(2), r.IsDBNull(3) ? null : r.GetInt64(3)));
        return points;
    }

    /// <summary>The newest reading's data units written; null when no reading carried the health log.</summary>
    public static long? LatestWrittenUnits(SqliteConnection conn, long driveId) =>
        HealthDb.Scalar<long?>(conn, "SELECT data_units_written FROM readings WHERE drive_id = $d AND data_units_written IS NOT NULL ORDER BY utc DESC LIMIT 1;", ("$d", driveId));

    public static LatestReading? Latest(SqliteConnection conn, long driveId)
    {
        // The newest reading, then the newest that carried the health log
        // (the same one for a drive read hourly). A drive whose log comes
        // daily through the RST reader keeps it between those reads.
        using var cmd = HealthDb.Command(conn, """
            SELECT n.utc, n.sensor_temp_c, n.warning_temp_c, n.error_log_newest, h.utc IS NOT NULL, h.critical_warning, h.available_spare, h.spare_threshold,
                   h.percentage_used, h.power_on_hours, h.power_cycles, h.unsafe_shutdowns, h.media_errors, h.warning_temp_minutes, h.critical_temp_minutes,
                   h.utc, h.source
            FROM (SELECT * FROM readings WHERE drive_id = $d ORDER BY utc DESC LIMIT 1) n
            LEFT JOIN (SELECT * FROM readings WHERE drive_id = $d AND health_page IS NOT NULL ORDER BY utc DESC LIMIT 1) h ON 1 = 1;
            """, ("$d", driveId));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        short? S(int i) => r.IsDBNull(i) ? null : (short)r.GetInt32(i);
        int? I(int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
        long? L(int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
        return new LatestReading(HealthDb.ParseIso(r.GetString(0)), S(1), S(2), L(3), r.GetBoolean(4), I(5), I(6), I(7), I(8), L(9), L(10), L(11), L(12), L(13), L(14),
            r.IsDBNull(15) ? null : HealthDb.ParseIso(r.GetString(15)), r.IsDBNull(16) ? null : r.GetString(16));
    }

    /// <summary>Each of the drive's volumes as last seen.</summary>
    public static IReadOnlyList<VolumeRow> LatestVolumes(SqliteConnection conn, long driveId)
    {
        var rows = new List<VolumeRow>();
        using var cmd = HealthDb.Command(conn, """
            SELECT letter, total_bytes, free_bytes, utc FROM volumes v
            WHERE drive_id = $d AND utc = (SELECT MAX(utc) FROM volumes WHERE drive_id = v.drive_id AND letter = v.letter)
            ORDER BY letter;
            """, ("$d", driveId));
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add(new VolumeRow(r.GetString(0), r.GetInt64(1), r.GetInt64(2), HealthDb.ParseIso(r.GetString(3))));
        return rows;
    }

    /// <summary>The temperature Windows' descriptor gave at each reading since <paramref name="since"/>, in °C.</summary>
    public static IReadOnlyList<TimePoint> Temperatures(SqliteConnection conn, long driveId, DateTimeOffset since)
    {
        var points = new List<TimePoint>();
        using var cmd = HealthDb.Command(conn,
            "SELECT utc, sensor_temp_c FROM readings WHERE drive_id = $d AND utc >= $s AND sensor_temp_c IS NOT NULL ORDER BY utc;",
            ("$d", driveId), ("$s", HealthDb.Iso(since)));
        using var r = cmd.ExecuteReader();
        while (r.Read()) points.Add(new TimePoint(HealthDb.ParseIso(r.GetString(0)), r.GetInt32(1)));
        return points;
    }

    /// <summary>
    /// Each volume's free bytes since <paramref name="since"/>, one point a
    /// local day (its last reading): free space moves slowly, and a year of
    /// hourly points is no clearer than a year of daily ones.
    /// </summary>
    public static IReadOnlyList<VolumeSeries> FreeSpace(SqliteConnection conn, long driveId, DateTimeOffset since)
    {
        var rows = new List<(string Letter, DateTimeOffset At, DateOnly Day, long Total, long Free)>();
        using var cmd = HealthDb.Command(conn,
            "SELECT letter, utc, local_date, total_bytes, free_bytes FROM volumes WHERE drive_id = $d AND utc >= $s ORDER BY letter, utc;",
            ("$d", driveId), ("$s", HealthDb.Iso(since)));
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add((r.GetString(0), HealthDb.ParseIso(r.GetString(1)), DateOnly.ParseExact(r.GetString(2), "yyyy-MM-dd"), r.GetInt64(3), r.GetInt64(4)));
        return rows.GroupBy(x => x.Letter).Select(g =>
        {
            var daily = Series.LastPerDay(g, x => x.Day);
            return new VolumeSeries(g.Key, daily[^1].Total, daily.Select(x => new TimePoint(x.At, x.Free)).ToList());
        }).ToList();
    }

    /// <summary>
    /// When the drive's newest error-log number last changed, from the readings:
    /// (since, value). A number that keeps climbing on a drive with no media
    /// errors means something keeps sending it requests it rejects. A 0 after
    /// a real number is a page that came back without entries, not an empty
    /// log (see HealthDb.ErrorLogNewest); readings before 2026-10-27 stored it.
    /// </summary>
    public static (DateTimeOffset Since, long Value)? ErrorLogSince(SqliteConnection conn, long driveId)
    {
        using var cmd = HealthDb.Command(conn, """
            SELECT utc, error_log_newest FROM readings
            WHERE drive_id = $d AND error_log_newest IS NOT NULL
              AND (error_log_newest > 0 OR NOT EXISTS (SELECT 1 FROM readings WHERE drive_id = $d AND error_log_newest > 0))
            ORDER BY utc DESC LIMIT 10000;
            """, ("$d", driveId));
        using var r = cmd.ExecuteReader();
        (DateTimeOffset Since, long Value)? found = null;
        while (r.Read())
        {
            var value = r.GetInt64(1);
            if (found is { } f && value != f.Value) break;
            found = (HealthDb.ParseIso(r.GetString(0)), value);
        }
        return found;
    }

    public static HistorySummary Summary(SqliteConnection conn)
    {
        DateTimeOffset? T(string? s) => s is null ? null : HealthDb.ParseIso(s);
        return new HistorySummary(
            HealthDb.CountReadings(conn),
            HealthDb.Scalar<long>(conn, "SELECT COUNT(*) FROM runs WHERE drives_read > 0;"),
            T(HealthDb.Scalar<string?>(conn, "SELECT MIN(utc) FROM readings;")),
            T(HealthDb.Scalar<string?>(conn, "SELECT MAX(started_utc) FROM runs WHERE status IN ('ok', 'partial');")),
            T(HealthDb.GetMeta(conn, "last_backup_utc")));
    }

    /// <summary>
    /// A fingerprint of the history that changes when a reading lands or a
    /// run starts or finishes (a run's alerts land with its readings): what
    /// the app compares before rebuilding a page.
    /// </summary>
    public static string Signature(SqliteConnection conn) =>
        HealthDb.Scalar<string?>(conn,
            "SELECT (SELECT COUNT(*) FROM readings) || '|' || COALESCE((SELECT id || status FROM runs ORDER BY id DESC LIMIT 1), '');") ?? "";

    public static IReadOnlyList<RunRow> Runs(SqliteConnection conn, int limit)
    {
        var rows = new List<RunRow>();
        using var cmd = HealthDb.Command(conn, "SELECT id, started_utc, trigger, status, drives_read, backup, duration_ms, error FROM runs ORDER BY id DESC LIMIT $n;", ("$n", limit));
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new RunRow(r.GetInt64(0), HealthDb.ParseIso(r.GetString(1)), r.GetString(2), r.GetString(3), r.GetInt32(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetInt64(6), r.IsDBNull(7) ? null : r.GetString(7)));
        return rows;
    }

    /// <summary>Storage events kept, by disk and kind, worst first; Windows' shutdown records are not among them.</summary>
    public static IReadOnlyList<EventGroup> StorageEvents(SqliteConnection conn)
    {
        var rows = new List<EventGroup>();
        if (!HasTable(conn, "events")) return rows;
        using var cmd = HealthDb.Command(conn, $"""
            SELECT disk, provider, event_id, MIN(level), COUNT(*), MIN(utc), MAX(utc) FROM events
            WHERE NOT ((provider = '{View.SystemEvents.KernelPower}' AND event_id = 41) OR (provider = '{View.SystemEvents.EventLog}' AND event_id = 6008))
            GROUP BY disk, provider, event_id ORDER BY disk IS NULL, disk, MIN(level), COUNT(*) DESC;
            """);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new EventGroup(r.IsDBNull(0) ? null : r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt64(4),
                HealthDb.ParseIso(r.GetString(5)), HealthDb.ParseIso(r.GetString(6))));
        return rows;
    }

    /// <summary>
    /// Unsafe shutdowns by day, newest first: Windows' record from the kept
    /// events, and each drive's own counter (the health log's Unsafe
    /// Shutdowns) as it grew between readings. A counter reset is not a shutdown.
    /// </summary>
    public static IReadOnlyList<ShutdownDay> UnsafeShutdowns(SqliteConnection conn, IEnumerable<long> driveIds)
    {
        var windows = new Dictionary<DateOnly, int>();
        if (HasTable(conn, "events"))
        {
            using var cmd = HealthDb.Command(conn, $"""
                SELECT local_date, MAX(SUM(provider = '{View.SystemEvents.KernelPower}' AND event_id = 41), SUM(provider = '{View.SystemEvents.EventLog}' AND event_id = 6008))
                FROM events GROUP BY local_date HAVING MAX(SUM(provider = '{View.SystemEvents.KernelPower}' AND event_id = 41), SUM(provider = '{View.SystemEvents.EventLog}' AND event_id = 6008)) > 0;
                """);
            using var r = cmd.ExecuteReader();
            while (r.Read()) windows[DateOnly.ParseExact(r.GetString(0), "yyyy-MM-dd")] = r.GetInt32(1);
        }
        var drives = new Dictionary<DateOnly, Dictionary<long, long>>();
        foreach (var id in driveIds)
        {
            var points = Counter(conn, id, "unsafe_shutdowns");
            for (var i = 1; i < points.Count; i++)
                if (points[i].Value - points[i - 1].Value is > 0 and var grew)
                {
                    var day = drives.TryGetValue(points[i].LocalDate, out var d) ? d : drives[points[i].LocalDate] = [];
                    day[id] = day.GetValueOrDefault(id) + grew;
                }
        }
        return windows.Keys.Union(drives.Keys).OrderDescending()
            .Select(d => new ShutdownDay(d, windows.GetValueOrDefault(d), drives.TryGetValue(d, out var x) ? x : new Dictionary<long, long>()))
            .ToList();
    }

    /// <summary>Each disk number as the newest reading of each drive had it: which drive an event's "Harddisk N" is.</summary>
    public static IReadOnlyDictionary<int, long> DiskNumbers(SqliteConnection conn)
    {
        var map = new Dictionary<int, long>();
        using var cmd = HealthDb.Command(conn, "SELECT drive_id, disk_number FROM readings r WHERE utc = (SELECT MAX(utc) FROM readings WHERE drive_id = r.drive_id AND source IS NULL) AND source IS NULL;");
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetInt32(1)] = r.GetInt64(0);
        return map;
    }

    private static bool HasTable(SqliteConnection conn, string table) =>
        HealthDb.Scalar<long>(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $t;", ("$t", table)) > 0;

    /// <summary>The newest alerts, newest first. A history not yet opened by a version-2 collector has none.</summary>
    public static IReadOnlyList<AlertRow> Alerts(SqliteConnection conn, int limit)
    {
        var rows = new List<AlertRow>();
        if (HealthDb.Scalar<long>(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'alerts';") == 0) return rows;
        using var cmd = HealthDb.Command(conn, "SELECT utc, drive_id, kind, level, title, body FROM alerts ORDER BY id DESC LIMIT $n;", ("$n", limit));
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new AlertRow(HealthDb.ParseIso(r.GetString(0)), r.IsDBNull(1) ? null : r.GetInt64(1), r.GetString(2),
                r.GetString(3) == "critical" ? AlertLevel.Critical : AlertLevel.Warning, r.GetString(4), r.GetString(5)));
        return rows;
    }

    /// <summary>Whether the history remembers this drive refusing this request on this firmware.</summary>
    public static bool IsRefused(SqliteConnection conn, string model, string deviceId, string request, string firmware) =>
        HealthDb.Scalar<long>(conn, """
            SELECT COUNT(*) FROM refused f JOIN drives d ON d.id = f.drive_id
            WHERE d.model = $m AND d.device_id = $i AND f.request = $r AND f.firmware = $f;
            """, ("$m", model), ("$i", deviceId), ("$r", request), ("$f", firmware)) > 0;

    public static IReadOnlyList<RefusalRow> Refusals(SqliteConnection conn)
    {
        var rows = new List<RefusalRow>();
        using var cmd = HealthDb.Command(conn, "SELECT d.model, f.request, f.firmware, f.first_utc, f.error FROM refused f JOIN drives d ON d.id = f.drive_id ORDER BY d.id;");
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add(new RefusalRow(r.GetString(0), r.GetString(1), r.GetString(2), HealthDb.ParseIso(r.GetString(3)), r.GetString(4)));
        return rows;
    }
}
