namespace NandWatch.Core.Data;

/// <summary>The database schema.</summary>
/// <remarks>
/// <para><b>Cumulative snapshots, never deltas.</b> Each reading stores the
/// drive's counters as it reported them, plus the raw 512-byte page. Rates and
/// daily totals are derived in queries (<see cref="Query.WriteHistory"/>), so
/// a parser fix or a new rule can recompute the whole history.</para>
/// <para><b>The schema only grows, by numbered migrations.</b> <c>CREATE
/// TABLE IF NOT EXISTS</c> does nothing to a table that already exists, so a
/// new column added to <see cref="Sql"/> never reaches a history created
/// before it. Version 1 is what the owner's PC has (since 2026-10-24); any
/// change after it bumps <see cref="Version"/> and adds an <c>ALTER TABLE</c>
/// step that <c>HealthDb.Open</c> applies to older files. A new TABLE needs no
/// step: <c>CREATE TABLE IF NOT EXISTS</c> adds it to an older file. Version 2
/// (2026-10-24) is the <c>alerts</c> table. Version 3 (2026-10-24) is
/// <c>readings.source</c>, the first column added after version 1: new files
/// get it from <see cref="Sql"/>, older ones from <c>HealthDb.Migrate</c>.</para>
/// <para><b>Counters are INTEGER</b> (64-bit). The drive's are 128-bit; a value
/// past 2^63 (about 4.7 zettabytes of data units) is stored as NULL, and the raw
/// page still holds it.</para>
/// </remarks>
public static class Schema
{
    public const int Version = 3;

    public const string Sql = """
        PRAGMA journal_mode = WAL;

        CREATE TABLE IF NOT EXISTS meta (
          key   TEXT PRIMARY KEY,
          value TEXT NOT NULL
        );

        -- A drive is model + Windows' device id (the namespace EUI-64 / NGUID).
        CREATE TABLE IF NOT EXISTS drives (
          id             INTEGER PRIMARY KEY AUTOINCREMENT,
          model          TEXT NOT NULL,
          device_id      TEXT NOT NULL,
          firmware       TEXT NOT NULL,
          first_seen_utc TEXT NOT NULL,
          last_seen_utc  TEXT NOT NULL,
          UNIQUE (model, device_id)
        );

        -- What the owner set for a drive. The rating overrides the built-in list.
        CREATE TABLE IF NOT EXISTS drive_settings (
          drive_id  INTEGER PRIMARY KEY REFERENCES drives(id),
          rated_tbw REAL,
          label     TEXT
        );

        -- One row per collector run. The history exists only if the collector
        -- runs, so a broken one must show.
        CREATE TABLE IF NOT EXISTS runs (
          id           INTEGER PRIMARY KEY AUTOINCREMENT,
          started_utc  TEXT NOT NULL,
          finished_utc TEXT,
          -- scheduled | app | sync | cli (the demo writes hourly and logon)
          trigger      TEXT NOT NULL,
          -- running | ok | partial | failed
          status       TEXT NOT NULL,
          drives_read  INTEGER NOT NULL DEFAULT 0,
          -- NULL when no backup was due; else ok or the failure.
          backup       TEXT,
          duration_ms  INTEGER,
          error        TEXT
        );

        CREATE TABLE IF NOT EXISTS readings (
          id          INTEGER PRIMARY KEY AUTOINCREMENT,
          run_id      INTEGER NOT NULL REFERENCES runs(id),
          drive_id    INTEGER NOT NULL REFERENCES drives(id),
          utc         TEXT    NOT NULL,
          -- Machine-local day and hour, computed at ingest from UTC.
          local_date  TEXT    NOT NULL,
          local_hour  INTEGER NOT NULL,
          disk_number INTEGER NOT NULL,
          -- Windows' temperature descriptor, read for every drive.
          sensor_temp_c  INTEGER,
          warning_temp_c INTEGER,
          -- The newest error-log entry's number. Read for every drive, so a
          -- drive's own error count can be watched even without its health
          -- log, and so it shows whether anything (this app included) keeps
          -- sending a drive requests it rejects. 0 only for a log that never
          -- held an entry; NULL when not read or not known.
          error_log_newest INTEGER,
          -- The health log: every column NULL when the drive did not answer.
          critical_warning      INTEGER,
          composite_temp_k      INTEGER,
          available_spare       INTEGER,
          spare_threshold       INTEGER,
          percentage_used       INTEGER,
          data_units_read       INTEGER,
          data_units_written    INTEGER,
          host_read_commands    INTEGER,
          host_write_commands   INTEGER,
          busy_minutes          INTEGER,
          power_cycles          INTEGER,
          power_on_hours        INTEGER,
          unsafe_shutdowns      INTEGER,
          media_errors          INTEGER,
          error_log_entries     INTEGER,
          warning_temp_minutes  INTEGER,
          critical_temp_minutes INTEGER,
          health_page           BLOB,
          -- Version 3. Where the reading came from: NULL, this collector's own
          -- unelevated read; 'rst', the elevated RST reader's daily health log.
          source                TEXT,
          UNIQUE (drive_id, utc)
        );
        CREATE INDEX IF NOT EXISTS idx_readings_drive ON readings(drive_id, utc);
        CREATE INDEX IF NOT EXISTS idx_readings_date  ON readings(local_date);

        CREATE TABLE IF NOT EXISTS volumes (
          run_id      INTEGER NOT NULL REFERENCES runs(id),
          drive_id    INTEGER NOT NULL REFERENCES drives(id),
          utc         TEXT    NOT NULL,
          local_date  TEXT    NOT NULL,
          letter      TEXT    NOT NULL,
          total_bytes INTEGER NOT NULL,
          free_bytes  INTEGER NOT NULL,
          UNIQUE (drive_id, letter, utc)
        );

        -- Error-log entries that can mean trouble (I/O queue or media status),
        -- kept before the drive's ring of entries overwrites them. Rejected
        -- admin commands are never stored: software asking for something a
        -- drive does not support makes those.
        CREATE TABLE IF NOT EXISTS media_errors (
          drive_id       INTEGER NOT NULL REFERENCES drives(id),
          error_count    INTEGER NOT NULL,
          queue          INTEGER NOT NULL,
          status         INTEGER NOT NULL,
          lba            INTEGER,
          namespace      INTEGER,
          first_seen_utc TEXT    NOT NULL,
          PRIMARY KEY (drive_id, error_count)
        );

        -- Requests a drive refused. Never sent to it again: each refusal adds
        -- an entry to the drive's own error log. Cleared when its firmware
        -- changes, or by hand (nandwatch collect --retry-refused).
        CREATE TABLE IF NOT EXISTS refused (
          drive_id  INTEGER NOT NULL REFERENCES drives(id),
          request   TEXT    NOT NULL,
          firmware  TEXT    NOT NULL,
          first_utc TEXT    NOT NULL,
          error     TEXT    NOT NULL,
          PRIMARY KEY (drive_id, request)
        );

        -- Version 2. What the collector told the owner (or would have, with
        -- notifications off): kept, because a notification can be missed.
        CREATE TABLE IF NOT EXISTS alerts (
          id       INTEGER PRIMARY KEY,
          utc      TEXT    NOT NULL,
          drive_id INTEGER REFERENCES drives(id),
          kind     TEXT    NOT NULL,
          level    TEXT    NOT NULL,
          title    TEXT    NOT NULL,
          body     TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_alerts_utc ON alerts(utc);

        -- Version 3. Storage and shutdown events copied from Windows' System
        -- log, which rolls over and does not survive a reset. Counts and
        -- identifiers only: no message text, which can hold paths.
        CREATE TABLE IF NOT EXISTS events (
          id         INTEGER PRIMARY KEY,
          utc        TEXT    NOT NULL,
          local_date TEXT    NOT NULL,
          provider   TEXT    NOT NULL,
          event_id   INTEGER NOT NULL,
          -- 1 critical, 2 error, 3 warning, 4 information, as the log has it.
          level      INTEGER NOT NULL,
          -- The disk the event names (\Device\HarddiskN, "Disk N"); NULL for none.
          disk       INTEGER,
          record_id  INTEGER NOT NULL,
          UNIQUE (provider, record_id, utc)
        );
        CREATE INDEX IF NOT EXISTS idx_events_utc ON events(utc);
        """;
}
