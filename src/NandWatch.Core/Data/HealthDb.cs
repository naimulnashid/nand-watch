using System.Globalization;
using Microsoft.Data.Sqlite;
using NandWatch.Core.Nvme;

namespace NandWatch.Core.Data;

/// <summary>Opening the database, and every write the collector makes.</summary>
public static class HealthDb
{
    /// <summary>ISO instants with milliseconds and a Z, as the siblings store them.</summary>
    public static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static DateTimeOffset ParseIso(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>The machine-local day and hour of an instant, fixed at ingest.</summary>
    public static (string Date, int Hour) Local(DateTimeOffset t, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(t, zone);
        return (local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), local.Hour);
    }

    /// <summary>
    /// A database on the system drive would be destroyed by the Windows reset
    /// the history is meant to outlive. Failing loudly now beats finding out
    /// after one.
    /// </summary>
    public static void AssertNotOnSystemDrive(string path)
    {
        if (AppPaths.IsOnSystemDrive(path))
            throw new InvalidOperationException(
                $"Refusing to use a database on the system drive: {Path.GetFullPath(path)}. " +
                "A Windows reset would erase it. Choose a data folder on another drive.");
    }

    /// <summary>Open for writing, creating the file and schema as needed.</summary>
    public static SqliteConnection Open(string path, bool allowSystemDrive = false)
    {
        if (!allowSystemDrive) AssertNotOnSystemDrive(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        conn.Open();
        Exec(conn, "PRAGMA busy_timeout = 10000;");
        Exec(conn, Schema.Sql);
        Migrate(conn);
        SetMeta(conn, "schema_version", Schema.Version.ToString(CultureInfo.InvariantCulture));
        return conn;
    }

    /// <summary>
    /// The steps that bring an older file up to <see cref="Schema.Version"/>.
    /// Each checks before it changes anything, so it can run on every open.
    /// </summary>
    private static void Migrate(SqliteConnection conn)
    {
        // Version 3: readings.source.
        if (!Columns(conn, "readings").Contains("source")) Exec(conn, "ALTER TABLE readings ADD COLUMN source TEXT;");
    }

    private static HashSet<string> Columns(SqliteConnection conn, string table)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = Command(conn, $"PRAGMA table_info({table});");
        using var r = cmd.ExecuteReader();
        while (r.Read()) names.Add(r.GetString(1));
        return names;
    }

    /// <summary>Open for reading, per use: a long-lived handle across WAL checkpoints serves stale numbers.</summary>
    public static SqliteConnection OpenRead(string path)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conn.Open();
        Exec(conn, "PRAGMA busy_timeout = 10000;");
        return conn;
    }

    public static long StartRun(SqliteConnection conn, string trigger, DateTimeOffset at) =>
        Scalar<long>(conn, "INSERT INTO runs (started_utc, trigger, status) VALUES ($s, $t, 'running') RETURNING id;", ("$s", Iso(at)), ("$t", trigger));

    public static void FinishRun(SqliteConnection conn, long id, string status, int drivesRead, string? backup, long durationMs, string? error, DateTimeOffset at) =>
        Exec(conn, "UPDATE runs SET finished_utc = $f, status = $s, drives_read = $d, backup = $b, duration_ms = $ms, error = $e WHERE id = $id;",
            ("$f", Iso(at)), ("$s", status), ("$d", drivesRead), ("$b", backup), ("$ms", durationMs), ("$e", error), ("$id", id));

    /// <summary>
    /// The backup's result, on a run already finished: the backup is written
    /// after <see cref="FinishRun"/>, so the copy it makes holds that run
    /// finished, not "running" for good.
    /// </summary>
    public static void SetRunBackup(SqliteConnection conn, long id, string backup, long durationMs, DateTimeOffset at) =>
        Exec(conn, "UPDATE runs SET backup = $b, duration_ms = $ms, finished_utc = $f WHERE id = $id;",
            ("$b", backup), ("$ms", durationMs), ("$f", Iso(at)), ("$id", id));

    /// <summary>The drive's row, created on first sight; its firmware and last sighting kept current.</summary>
    public static long UpsertDrive(SqliteConnection conn, DriveIdentity drive, DateTimeOffset at) =>
        Scalar<long>(conn, """
            INSERT INTO drives (model, device_id, firmware, first_seen_utc, last_seen_utc) VALUES ($m, $d, $f, $t, $t)
            ON CONFLICT (model, device_id) DO UPDATE SET firmware = excluded.firmware, last_seen_utc = excluded.last_seen_utc
            RETURNING id;
            """, ("$m", drive.Model), ("$d", drive.DeviceId), ("$f", drive.Firmware), ("$t", Iso(at)));

    public static void InsertReading(SqliteConnection conn, long runId, long driveId, DriveReading r, TimeZoneInfo zone)
    {
        var (date, hour) = Local(r.At, zone);
        var h = r.Health;
        Exec(conn, """
            INSERT OR IGNORE INTO readings (
              run_id, drive_id, utc, local_date, local_hour, disk_number, sensor_temp_c, warning_temp_c, error_log_newest,
              critical_warning, composite_temp_k, available_spare, spare_threshold, percentage_used,
              data_units_read, data_units_written, host_read_commands, host_write_commands, busy_minutes,
              power_cycles, power_on_hours, unsafe_shutdowns, media_errors, error_log_entries,
              warning_temp_minutes, critical_temp_minutes, health_page, source)
            VALUES ($run, $drive, $utc, $date, $hour, $disk, $st, $wt, $eln,
              $cw, $ct, $sp, $th, $pu, $dur, $duw, $hrc, $hwc, $busy, $pc, $poh, $us, $me, $ele, $wtm, $ctm, $page, $src);
            """,
            ("$run", runId), ("$drive", driveId), ("$utc", Iso(r.At)), ("$date", date), ("$hour", hour), ("$disk", r.DiskNumber),
            ("$st", r.SensorTemperatureC), ("$wt", r.WarningTemperatureC),
            ("$eln", ErrorLogNewest(conn, driveId, r.ErrorEntries)),
            ("$cw", h is null ? null : (byte)h.CriticalWarning), ("$ct", h?.CompositeTemperatureK), ("$sp", h?.AvailableSpare),
            ("$th", h?.AvailableSpareThreshold), ("$pu", h?.PercentageUsed),
            ("$dur", Db(h?.DataUnitsRead)), ("$duw", Db(h?.DataUnitsWritten)), ("$hrc", Db(h?.HostReadCommands)), ("$hwc", Db(h?.HostWriteCommands)),
            ("$busy", Db(h?.ControllerBusyMinutes)), ("$pc", Db(h?.PowerCycles)), ("$poh", Db(h?.PowerOnHours)), ("$us", Db(h?.UnsafeShutdowns)),
            ("$me", Db(h?.MediaErrors)), ("$ele", Db(h?.ErrorLogEntries)),
            ("$wtm", h?.WarningTemperatureMinutes), ("$ctm", h?.CriticalTemperatureMinutes), ("$page", r.HealthPage), ("$src", r.Source));
    }

    /// <summary>
    /// The newest entry's number, 0 for a log that has never held one, or NULL
    /// when unknown. A page with no used entries from a drive that has shown
    /// entries before is not an empty log: error numbers only grow, and the
    /// Samsung behind RST returns such pages after a boot or resume while its
    /// own count runs into the thousands (2026-10-27). Stored as 0 it read as
    /// "empty" and broke "unchanged since".
    /// </summary>
    private static long? ErrorLogNewest(SqliteConnection conn, long driveId, IReadOnlyList<ErrorLogEntry>? entries)
    {
        if (entries is null) return null;
        if (entries.Count > 0) return Db(entries.Max(e => e.ErrorCount));
        return Scalar<long>(conn, "SELECT EXISTS (SELECT 1 FROM readings WHERE drive_id = $d AND error_log_newest > 0);", ("$d", driveId)) == 1 ? null : 0;
    }

    public static void InsertVolume(SqliteConnection conn, long runId, long driveId, VolumeReading v, DateTimeOffset at, TimeZoneInfo zone) =>
        Exec(conn, "INSERT OR IGNORE INTO volumes (run_id, drive_id, utc, local_date, letter, total_bytes, free_bytes) VALUES ($r, $d, $u, $l, $v, $t, $f);",
            ("$r", runId), ("$d", driveId), ("$u", Iso(at)), ("$l", Local(at, zone).Date), ("$v", v.Letter), ("$t", v.TotalBytes), ("$f", v.FreeBytes));

    /// <summary>Keeps the entries that can mean trouble; returns how many were new.</summary>
    public static int RecordMediaErrors(SqliteConnection conn, long driveId, IEnumerable<ErrorLogEntry> entries, DateTimeOffset at)
    {
        var added = 0;
        foreach (var e in entries.Where(e => e.IsMediaOrIo))
            added += Exec(conn, """
                INSERT OR IGNORE INTO media_errors (drive_id, error_count, queue, status, lba, namespace, first_seen_utc)
                VALUES ($d, $c, $q, $s, $l, $n, $t);
                """, ("$d", driveId), ("$c", Db(e.ErrorCount)), ("$q", e.SubmissionQueue), ("$s", e.Status), ("$l", Db(e.Lba)), ("$n", e.Namespace), ("$t", Iso(at)));
        return added;
    }

    /// <summary>
    /// True when the drive refused this request before, on the firmware it
    /// runs now. A firmware update is a fresh chance; anything else is not.
    /// </summary>
    public static bool IsRefused(SqliteConnection conn, long driveId, string request, string firmware) =>
        Scalar<long>(conn, "SELECT COUNT(*) FROM refused WHERE drive_id = $d AND request = $r AND firmware = $f;", ("$d", driveId), ("$r", request), ("$f", firmware)) > 0;

    public static void RecordRefusal(SqliteConnection conn, long driveId, string request, string firmware, string error, DateTimeOffset at) =>
        Exec(conn, """
            INSERT INTO refused (drive_id, request, firmware, first_utc, error) VALUES ($d, $r, $f, $t, $e)
            ON CONFLICT (drive_id, request) DO UPDATE SET firmware = excluded.firmware, first_utc = excluded.first_utc, error = excluded.error;
            """, ("$d", driveId), ("$r", request), ("$f", firmware), ("$t", Iso(at)), ("$e", error));

    public static int ClearRefusals(SqliteConnection conn) => Exec(conn, "DELETE FROM refused;");

    /// <summary>A backup is due once a local day, so readings lost between backups cost resolution, never totals.</summary>
    public static bool BackupDue(SqliteConnection conn, DateTimeOffset now, TimeZoneInfo zone) =>
        GetMeta(conn, "last_backup_utc") is not { } last || Local(ParseIso(last), zone).Date != Local(now, zone).Date;

    /// <summary>Fold the WAL into the main file, so the synced live copy's three files disagree only for a moment.</summary>
    public static void Checkpoint(SqliteConnection conn)
    {
        try { Exec(conn, "PRAGMA wal_checkpoint(TRUNCATE);"); }
        catch (SqliteException) { }
    }

    /// <summary>
    /// Write the restore source: SQLite's backup API into a temporary file,
    /// switched to one-file DELETE journaling, then moved over the old one.
    /// Returns "ok" or the failure; a failed backup never fails the run.
    /// </summary>
    public static string Backup(SqliteConnection conn, string backupPath, DateTimeOffset at)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(backupPath))!);
            var tmp = backupPath + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tmp, Pooling = false }.ToString()))
            {
                dest.Open();
                conn.BackupDatabase(dest);
                Exec(dest, "PRAGMA journal_mode = DELETE;");
            }
            File.Move(tmp, backupPath, overwrite: true);
            SetMeta(conn, "last_backup_utc", Iso(at));
            return "ok";
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return "failed: " + ex.Message;
        }
    }

    /// <summary>What the owner set for a drive: its rating (overriding the built-in list) and a label. Null clears either.</summary>
    public static void SetDriveSettings(SqliteConnection conn, long driveId, decimal? ratedTbw, string? label) =>
        Exec(conn, """
            INSERT INTO drive_settings (drive_id, rated_tbw, label) VALUES ($d, $r, $l)
            ON CONFLICT (drive_id) DO UPDATE SET rated_tbw = excluded.rated_tbw, label = excluded.label;
            """, ("$d", driveId), ("$r", ratedTbw is { } r ? (double)r : null), ("$l", label));

    public static long CountReadings(SqliteConnection conn) => Scalar<long>(conn, "SELECT COUNT(*) FROM readings;");

    /// <summary>Keeps one System-log event; false when it was already kept.</summary>
    public static bool InsertEvent(SqliteConnection conn, SystemEvent e, TimeZoneInfo zone) =>
        Exec(conn, "INSERT OR IGNORE INTO events (utc, local_date, provider, event_id, level, disk, record_id) VALUES ($u, $l, $p, $i, $v, $d, $r);",
            ("$u", Iso(e.At)), ("$l", Local(e.At, zone).Date), ("$p", e.Provider), ("$i", e.Id), ("$v", e.Level), ("$d", e.Disk), ("$r", e.RecordId)) > 0;

    /// <summary>Keeps an alert in the history; <paramref name="driveId"/> is null for one about collection itself.</summary>
    public static void InsertAlert(SqliteConnection conn, long? driveId, Query.Alert alert, DateTimeOffset at) =>
        Exec(conn, "INSERT INTO alerts (utc, drive_id, kind, level, title, body) VALUES ($u, $d, $k, $l, $t, $b);",
            ("$u", Iso(at)), ("$d", driveId), ("$k", alert.Kind), ("$l", alert.Level == Query.AlertLevel.Critical ? "critical" : "warning"), ("$t", alert.Title), ("$b", alert.Body));

    public static string? GetMeta(SqliteConnection conn, string key) =>
        Scalar<string?>(conn, "SELECT value FROM meta WHERE key = $k;", ("$k", key));

    public static void SetMeta(SqliteConnection conn, string key, string value) =>
        Exec(conn, "INSERT INTO meta (key, value) VALUES ($k, $v) ON CONFLICT (key) DO UPDATE SET value = excluded.value;", ("$k", key), ("$v", value));

    /// <summary>A 128-bit counter as SQLite's 64-bit INTEGER, or NULL past it (the raw page keeps the value).</summary>
    internal static long? Db(UInt128? v) => v is { } x && x <= long.MaxValue ? (long)x : null;

    internal static long? Db(ulong v) => v <= long.MaxValue ? (long)v : null;

    internal static int Exec(SqliteConnection conn, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(conn, sql, args);
        return cmd.ExecuteNonQuery();
    }

    internal static T Scalar<T>(SqliteConnection conn, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(conn, sql, args);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? default! : (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), CultureInfo.InvariantCulture);
    }

    internal static SqliteCommand Command(SqliteConnection conn, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }
}
