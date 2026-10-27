using NandWatch.Core.Data;
using NandWatch.Core.Query;
using NandWatch.Core.View;

namespace NandWatch.Core.Tests;

public class EventTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Events_are_kept_once_and_grouped_by_disk_and_kind()
    {
        using var db = new TestDb();
        var e1 = new SystemEvent(T0, "disk", 7, 2, 2, 100);
        Assert.True(HealthDb.InsertEvent(db.Conn, e1, TimeZoneInfo.Utc));
        Assert.False(HealthDb.InsertEvent(db.Conn, e1, TimeZoneInfo.Utc));   // the same record, read again
        HealthDb.InsertEvent(db.Conn, new SystemEvent(T0.AddHours(1), "disk", 7, 2, 2, 101), TimeZoneInfo.Utc);
        HealthDb.InsertEvent(db.Conn, new SystemEvent(T0, "stornvme", 129, 3, 1, 102), TimeZoneInfo.Utc);
        HealthDb.InsertEvent(db.Conn, new SystemEvent(T0, SystemEvents.KernelPower, 41, 1, null, 103), TimeZoneInfo.Utc);

        var groups = HealthQueries.StorageEvents(db.Conn);
        Assert.Equal(2, groups.Count);   // the shutdown record is not a storage event
        Assert.Equal((1, "stornvme"), (groups[0].Disk!.Value, groups[0].Provider));
        Assert.Equal((2, 2L, T0.AddHours(1)), (groups[1].Disk!.Value, groups[1].Count, groups[1].Last));
        Assert.Equal("Bad block", SystemEvents.Describe("disk", 7));
    }

    [Fact]
    public void Unsafe_shutdowns_join_Windows_record_and_each_drives_counter_by_day()
    {
        using var db = new TestDb();
        // Windows wrote both records for one incident on the 21st: one shutdown, not two.
        HealthDb.InsertEvent(db.Conn, new SystemEvent(T0.AddDays(1), SystemEvents.KernelPower, 41, 1, null, 1), TimeZoneInfo.Utc);
        HealthDb.InsertEvent(db.Conn, new SystemEvent(T0.AddDays(1), SystemEvents.EventLog, 6008, 2, null, 2), TimeZoneInfo.Utc);

        var drive = new DriveIdentity("Example", "0000_0000_0000_0031.", "FW1");
        var id = HealthDb.UpsertDrive(db.Conn, drive, T0);
        var page = Pages.Health();
        foreach (var (at, count) in new[] { (T0, 50), (T0.AddDays(1), 51), (T0.AddDays(2), 51), (T0.AddDays(3), 53) })
        {
            var p = (byte[])page.Clone();
            Pages.U128(p, 144, (UInt128)count);   // Unsafe Shutdowns
            var run = HealthDb.StartRun(db.Conn, "test", at);
            HealthDb.InsertReading(db.Conn, run, id, new DriveReading { Drive = drive, DiskNumber = 1, At = at, Health = Nvme.HealthLog.Parse(p), HealthPage = p }, TimeZoneInfo.Utc);
        }

        var days = HealthQueries.UnsafeShutdowns(db.Conn, [id]);
        Assert.Equal([new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 21)], days.Select(d => d.Date));
        Assert.Equal((0, 2L), (days[0].Windows, days[0].Drives[id]));   // the drive saw two Windows did not record
        Assert.Equal((1, 1L), (days[1].Windows, days[1].Drives[id]));
        Assert.Equal(id, HealthQueries.DiskNumbers(db.Conn)[1]);
    }
}
