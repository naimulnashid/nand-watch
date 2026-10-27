using System.Buffers.Binary;
using Microsoft.Data.Sqlite;
using NandWatch.Core.Data;
using NandWatch.Core.Nvme;

namespace NandWatch.Core.Tests;

public class DbTests
{
    private static readonly TimeZoneInfo Dhaka = TimeZoneInfo.CreateCustomTimeZone("UTC+6", TimeSpan.FromHours(6), "UTC+6", "UTC+6");
    private static readonly DriveIdentity Drive = new("Example NVMe 512GB", "0000_0000_0000_0001.", "FW1");

    private static DriveReading Reading(DateTimeOffset at, Action<Span<byte>>? edit = null)
    {
        var page = Pages.Health(edit);
        return new DriveReading { Drive = Drive, DiskNumber = 1, At = at, Health = HealthLog.Parse(page), HealthPage = page, SensorTemperatureC = 40, WarningTemperatureC = 85 };
    }

    [Fact]
    public void A_reading_is_stored_with_its_local_day_and_hour_and_raw_page()
    {
        using var db = new TestDb();
        var at = new DateTimeOffset(2026, 10, 24, 20, 30, 0, TimeSpan.Zero);   // 02:30 the next day in UTC+6
        var run = HealthDb.StartRun(db.Conn, "cli", at);
        var drive = HealthDb.UpsertDrive(db.Conn, Drive, at);
        HealthDb.InsertReading(db.Conn, run, drive, Reading(at), Dhaka);

        using var cmd = HealthDb.Command(db.Conn, "SELECT local_date, local_hour, data_units_written, length(health_page), sensor_temp_c FROM readings;");
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal("2026-10-25", r.GetString(0));
        Assert.Equal(2, r.GetInt32(1));
        Assert.Equal(1_953_125, r.GetInt64(2));
        Assert.Equal(512, r.GetInt32(3));
        Assert.Equal(40, r.GetInt32(4));
    }

    [Fact]
    public void A_counter_past_64_bits_is_stored_as_null_and_the_page_keeps_it()
    {
        using var db = new TestDb();
        var at = DateTimeOffset.UnixEpoch;
        var big = (UInt128)long.MaxValue + 1;
        var run = HealthDb.StartRun(db.Conn, "cli", at);
        var drive = HealthDb.UpsertDrive(db.Conn, Drive, at);
        HealthDb.InsertReading(db.Conn, run, drive, Reading(at, p => Pages.U128(p, 48, big)), Dhaka);
        Assert.Equal(0L, HealthDb.Scalar<long>(db.Conn, "SELECT COUNT(*) FROM readings WHERE data_units_written IS NOT NULL;"));
        var page = HealthDb.Scalar<byte[]>(db.Conn, "SELECT health_page FROM readings;");
        Assert.Equal(big, HealthLog.Parse(page).DataUnitsWritten);
    }

    [Fact]
    public void The_same_drive_is_one_row_and_its_firmware_stays_current()
    {
        using var db = new TestDb();
        var a = HealthDb.UpsertDrive(db.Conn, Drive, DateTimeOffset.UnixEpoch);
        var b = HealthDb.UpsertDrive(db.Conn, Drive with { Firmware = "FW2" }, DateTimeOffset.UnixEpoch.AddHours(1));
        Assert.Equal(a, b);
        Assert.Equal("FW2", HealthDb.Scalar<string>(db.Conn, "SELECT firmware FROM drives;"));
    }

    [Fact]
    public void A_refusal_is_remembered_until_the_firmware_changes()
    {
        using var db = new TestDb();
        var drive = HealthDb.UpsertDrive(db.Conn, Drive, DateTimeOffset.UnixEpoch);
        Assert.False(HealthDb.IsRefused(db.Conn, drive, "health", "FW1"));
        HealthDb.RecordRefusal(db.Conn, drive, "health", "FW1", "1: Incorrect function.", DateTimeOffset.UnixEpoch);
        Assert.True(HealthDb.IsRefused(db.Conn, drive, "health", "FW1"));
        Assert.False(HealthDb.IsRefused(db.Conn, drive, "health", "FW2"));
        HealthDb.ClearRefusals(db.Conn);
        Assert.False(HealthDb.IsRefused(db.Conn, drive, "health", "FW1"));
    }

    [Fact]
    public void Only_media_and_IO_error_entries_are_kept_and_each_once()
    {
        using var db = new TestDb();
        var drive = HealthDb.UpsertDrive(db.Conn, Drive, DateTimeOffset.UnixEpoch);
        ErrorLogEntry[] entries =
        [
            new(11627, 0, 1, 0x0B << 1, 0, 1),        // rejected admin command: not kept
            new(12, 3, 7, 2 << 9 | 0x81 << 1, 4096, 1), // unrecovered read error on an I/O queue
        ];
        Assert.Equal(1, HealthDb.RecordMediaErrors(db.Conn, drive, entries, DateTimeOffset.UnixEpoch));
        Assert.Equal(0, HealthDb.RecordMediaErrors(db.Conn, drive, entries, DateTimeOffset.UnixEpoch.AddHours(1)));
        Assert.Equal(12L, HealthDb.Scalar<long>(db.Conn, "SELECT error_count FROM media_errors;"));
    }

    [Fact]
    public void A_backup_is_due_once_a_local_day_and_is_one_self_contained_file()
    {
        using var db = new TestDb();
        var morning = new DateTimeOffset(2026, 10, 24, 2, 0, 0, TimeSpan.Zero);   // 08:00 in UTC+6
        Assert.True(HealthDb.BackupDue(db.Conn, morning, Dhaka));

        var backup = System.IO.Path.Combine(db.Dir, "nand-watch.db");
        Assert.Equal("ok", HealthDb.Backup(db.Conn, backup, morning));
        Assert.False(HealthDb.BackupDue(db.Conn, morning.AddHours(15), Dhaka));   // 23:00 the same local day
        Assert.True(HealthDb.BackupDue(db.Conn, morning.AddHours(17), Dhaka));    // 01:00 the next

        Assert.False(File.Exists(backup + "-wal"));
        using var copy = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False");
        copy.Open();
        Assert.Equal("delete", HealthDb.Scalar<string>(copy, "PRAGMA journal_mode;"));
        Assert.Equal(Schema.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), HealthDb.Scalar<string>(copy, "SELECT value FROM meta WHERE key = 'schema_version';"));
    }

    [Fact]
    public void A_backup_written_after_its_run_finished_restores_that_run_finished()
    {
        using var db = new TestDb();
        var at = new DateTimeOffset(2026, 10, 27, 18, 7, 0, TimeSpan.Zero);
        var run = HealthDb.StartRun(db.Conn, "scheduled", at);
        HealthDb.FinishRun(db.Conn, run, "ok", 2, null, 300, null, at);
        var backup = System.IO.Path.Combine(db.Dir, "nand-watch.db");
        HealthDb.SetRunBackup(db.Conn, run, HealthDb.Backup(db.Conn, backup, at), 900, at);

        Assert.Equal("ok", HealthDb.Scalar<string>(db.Conn, "SELECT backup FROM runs;"));
        Assert.Equal(900L, HealthDb.Scalar<long>(db.Conn, "SELECT duration_ms FROM runs;"));
        using var copy = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False");
        copy.Open();
        Assert.Equal("ok", HealthDb.Scalar<string>(copy, "SELECT status FROM runs;"));
        Assert.Equal(2L, HealthDb.Scalar<long>(copy, "SELECT drives_read FROM runs;"));
    }
}

public class ErrorLogSinceTests
{
    [Fact]
    public void The_newest_error_number_is_stored_and_its_last_change_found()
    {
        using var db = new TestDb();
        var drive = new DriveIdentity("Example", "0000_0000_0000_0002.", "FW1");
        var id = HealthDb.UpsertDrive(db.Conn, drive, DateTimeOffset.UnixEpoch);
        var t0 = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        ulong[] numbers = [11627, 11628, 11628, 11628];
        for (var i = 0; i < numbers.Length; i++)
        {
            var run = HealthDb.StartRun(db.Conn, "cli", t0.AddHours(i));
            HealthDb.InsertReading(db.Conn, run, id, new DriveReading
            {
                Drive = drive, DiskNumber = 0, At = t0.AddHours(i),
                ErrorEntries = [new ErrorLogEntry(numbers[i], 0, 1, 0x0B << 1, 0, 1), new ErrorLogEntry(numbers[i] - 1, 0, 1, 0x0B << 1, 0, 1)],
            }, TimeZoneInfo.Utc);
        }
        var since = NandWatch.Core.Query.HealthQueries.ErrorLogSince(db.Conn, id);
        Assert.Equal((t0.AddHours(1), 11628L), since);
    }

    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);

    private static void Insert(TestDb db, DriveIdentity drive, long id, int hour, params ulong[] numbers)
    {
        var run = HealthDb.StartRun(db.Conn, "cli", T0.AddHours(hour));
        HealthDb.InsertReading(db.Conn, run, id, new DriveReading
        {
            Drive = drive, DiskNumber = 0, At = T0.AddHours(hour),
            ErrorEntries = numbers.Select(n => new ErrorLogEntry(n, 0, 1, 0x0B << 1, 0, 1)).ToList(),
        }, TimeZoneInfo.Utc);
    }

    [Fact]
    public void A_page_without_entries_after_real_numbers_is_unknown_not_empty()
    {
        using var db = new TestDb();
        var drive = new DriveIdentity("Example", "0000_0000_0000_0004.", "FW1");
        var id = HealthDb.UpsertDrive(db.Conn, drive, DateTimeOffset.UnixEpoch);
        Insert(db, drive, id, 0, 11679);
        Insert(db, drive, id, 1, 11680);
        Insert(db, drive, id, 2);   // the page came back with no used entries
        Insert(db, drive, id, 3);

        Assert.Equal(2L, HealthDb.Scalar<long>(db.Conn, "SELECT COUNT(*) FROM readings WHERE error_log_newest IS NULL;"));
        Assert.Equal((T0.AddHours(1), 11680L), NandWatch.Core.Query.HealthQueries.ErrorLogSince(db.Conn, id));
    }

    [Fact]
    public void A_log_that_never_held_an_entry_is_empty()
    {
        using var db = new TestDb();
        var drive = new DriveIdentity("Example", "0000_0000_0000_0005.", "FW1");
        var id = HealthDb.UpsertDrive(db.Conn, drive, DateTimeOffset.UnixEpoch);
        Insert(db, drive, id, 0);
        Insert(db, drive, id, 1);

        Assert.Equal((T0, 0L), NandWatch.Core.Query.HealthQueries.ErrorLogSince(db.Conn, id));
    }

    [Fact]
    public void Zeros_stored_by_older_collectors_after_real_numbers_are_skipped()
    {
        using var db = new TestDb();
        var drive = new DriveIdentity("Example", "0000_0000_0000_0006.", "FW1");
        var id = HealthDb.UpsertDrive(db.Conn, drive, DateTimeOffset.UnixEpoch);
        Insert(db, drive, id, 0, 11676);
        Insert(db, drive, id, 1, 11677);
        Insert(db, drive, id, 2, 11677);
        Insert(db, drive, id, 3, 1);
        HealthDb.Exec(db.Conn, "UPDATE readings SET error_log_newest = 0 WHERE utc = $u;", ("$u", HealthDb.Iso(T0.AddHours(3))));

        Assert.Equal((T0.AddHours(1), 11677L), NandWatch.Core.Query.HealthQueries.ErrorLogSince(db.Conn, id));
    }
}

public class WindowStartTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_window_starts_at_the_first_reading_when_the_history_is_shorter()
    {
        var first = Now.AddDays(-3);
        Assert.Equal(first, NandWatch.Core.Query.HealthQueries.WindowStart(Now, 90, first));
        Assert.Equal(Now.AddDays(-2), NandWatch.Core.Query.HealthQueries.WindowStart(Now, 2, first));
        Assert.Equal(Now.AddDays(-7), NandWatch.Core.Query.HealthQueries.WindowStart(Now, 7, null));
        Assert.Equal(Now.AddHours(-1), NandWatch.Core.Query.HealthQueries.WindowStart(Now, 7, Now.AddMinutes(-5)));
    }

    [Fact]
    public void The_first_reading_is_the_drives_oldest()
    {
        using var db = new TestDb();
        var drive = new DriveIdentity("Example", "0000_0000_0000_0007.", "FW1");
        var id = HealthDb.UpsertDrive(db.Conn, drive, DateTimeOffset.UnixEpoch);
        Assert.Null(NandWatch.Core.Query.HealthQueries.FirstReading(db.Conn, id));
        foreach (var h in new[] { 5, 2, 9 })
        {
            var run = HealthDb.StartRun(db.Conn, "cli", Now.AddHours(h));
            HealthDb.InsertReading(db.Conn, run, id, new DriveReading { Drive = drive, DiskNumber = 0, At = Now.AddHours(h) }, TimeZoneInfo.Utc);
        }
        Assert.Equal(Now.AddHours(2), NandWatch.Core.Query.HealthQueries.FirstReading(db.Conn, id));
    }
}

public class SummaryTests
{
    [Fact]
    public void The_summary_counts_readings_and_finds_the_last_good_run()
    {
        using var db = new TestDb();
        var drive = new DriveIdentity("Example", "0000_0000_0000_0003.", "FW1");
        var id = HealthDb.UpsertDrive(db.Conn, drive, DateTimeOffset.UnixEpoch);
        var t0 = new DateTimeOffset(2026, 10, 24, 7, 7, 0, TimeSpan.Zero);
        var ok = HealthDb.StartRun(db.Conn, "scheduled", t0);
        HealthDb.InsertReading(db.Conn, ok, id, new DriveReading { Drive = drive, DiskNumber = 0, At = t0 }, TimeZoneInfo.Utc);
        HealthDb.FinishRun(db.Conn, ok, "ok", 1, null, 40, null, t0);
        var bad = HealthDb.StartRun(db.Conn, "scheduled", t0.AddHours(1));
        HealthDb.FinishRun(db.Conn, bad, "failed", 0, null, 40, "x", t0.AddHours(1));

        var s = NandWatch.Core.Query.HealthQueries.Summary(db.Conn);
        Assert.Equal(1, s.Readings);
        Assert.Equal(t0, s.First);
        Assert.Equal(t0, s.LastOk);
        Assert.Null(s.LastBackup);
    }
}
