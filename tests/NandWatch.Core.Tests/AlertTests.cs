using NandWatch.Core.Data;
using NandWatch.Core.Query;

namespace NandWatch.Core.Tests;

public class AlertTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 7, 7, 0, TimeSpan.Zero);

    private static LatestReading R(int warning = 0, int spare = 100, int used = 21, long media = 0, long warnMin = 0, long critMin = 0, short? temp = 40, bool health = true) =>
        new(T0, temp, 85, 11_630, health, health ? warning : null, health ? spare : null, health ? 5 : null, health ? used : null,
            14_615, 1_387, 50, health ? media : null, health ? warnMin : null, health ? critMin : null);

    [Fact]
    public void The_first_reading_is_the_baseline() =>
        Assert.Empty(Alerts.Compare("Drive", null, R(warning: 4, spare: 3, media: 9)));

    [Fact]
    public void A_healthy_drive_raises_nothing() => Assert.Empty(Alerts.Compare("Drive", R(), R(), 32.5m, 32.6m));

    [Fact]
    public void Each_new_problem_is_raised_once()
    {
        var before = R();
        var after = R(warning: 0b101, spare: 4, media: 2, critMin: 3);
        var alerts = Alerts.Compare("Drive", before, after);
        Assert.Equal(["critical-warning", "spare", "media-errors", "critical-temperature"], alerts.Select(a => a.Kind));
        Assert.All(alerts, a => Assert.Equal(AlertLevel.Critical, a.Level));
        Assert.Contains("Spare below its threshold; reliability degraded", alerts[0].Body);
        // The next hour, nothing changed: nothing is said again.
        Assert.Empty(Alerts.Compare("Drive", after, after));
    }

    [Fact]
    public void Wear_crossing_a_step_is_amber_on_both_figures()
    {
        var alerts = Alerts.Compare("Drive", R(used: 79), R(used: 80), 89.9m, 90.1m);
        Assert.Equal(["wear-vendor-80", "wear-written-90"], alerts.Select(a => a.Kind));
        Assert.All(alerts, a => Assert.Equal(AlertLevel.Warning, a.Level));
    }

    [Fact]
    public void Without_a_health_log_the_sensor_decides_running_hot()
    {
        var alerts = Alerts.Compare("Drive", R(health: false, temp: 80), R(health: false, temp: 85));
        Assert.Equal("warning-temperature", Assert.Single(alerts).Kind);
        Assert.Empty(Alerts.Compare("Drive", R(health: false, temp: 86), R(health: false, temp: 87)));
    }

    [Fact]
    public void New_media_entries_in_the_error_log_are_red() =>
        Assert.Equal(AlertLevel.Critical, Assert.Single(Alerts.Compare("Drive", R(health: false), R(health: false), newMediaEntries: 1)).Level);

    [Fact]
    public void Alerts_are_kept_and_read_back_newest_first()
    {
        using var db = new TestDb();
        HealthDb.InsertAlert(db.Conn, null, new Alert("collection", AlertLevel.Warning, "a", "b"), T0);
        HealthDb.InsertAlert(db.Conn, null, new Alert("spare", AlertLevel.Critical, "c", "d"), T0.AddHours(1));
        var rows = HealthQueries.Alerts(db.Conn, 10);
        Assert.Equal(["spare", "collection"], rows.Select(r => r.Kind));
        Assert.Equal(AlertLevel.Critical, rows[0].Level);
    }
}

public class SchemaTests
{
    [Fact]
    public void A_version_1_history_gains_the_alerts_table_on_open()
    {
        using var db = new TestDb();
        using (var cmd = db.Conn.CreateCommand())
        {
            cmd.CommandText = "DROP TABLE alerts; UPDATE meta SET value = '1' WHERE key = 'schema_version';";
            cmd.ExecuteNonQuery();
        }
        Assert.Empty(HealthQueries.Alerts(db.Conn, 5));   // read before any collector opened it: no table, no failure
        using var again = HealthDb.Open(db.Path, allowSystemDrive: true);
        HealthDb.InsertAlert(again, null, new Alert("x", AlertLevel.Warning, "t", "b"), DateTimeOffset.UnixEpoch);
        Assert.Single(HealthQueries.Alerts(again, 5));
        Assert.Equal(Schema.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), HealthDb.GetMeta(again, "schema_version"));
    }

    [Fact]
    public void A_history_without_the_source_column_gains_it_on_open()
    {
        using var db = new TestDb();
        using (var cmd = db.Conn.CreateCommand())
        {
            cmd.CommandText = "ALTER TABLE readings DROP COLUMN source;";
            cmd.ExecuteNonQuery();
        }
        using var again = HealthDb.Open(db.Path, allowSystemDrive: true);
        var drive = new DriveIdentity("Example", "0000_0000_0000_0011.", "FW1");
        var id = HealthDb.UpsertDrive(again, drive, DateTimeOffset.UnixEpoch);
        var run = HealthDb.StartRun(again, "test", DateTimeOffset.UnixEpoch);
        HealthDb.InsertReading(again, run, id, new DriveReading { Drive = drive, DiskNumber = 0, At = DateTimeOffset.UnixEpoch, Source = DriveReading.RstSource }, TimeZoneInfo.Utc);
        Assert.Equal(1, HealthDb.CountReadings(again));
    }
}
