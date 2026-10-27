using System.Text.Json;
using NandWatch.Core.Data;
using NandWatch.Core.Nvme;
using NandWatch.Core.Query;

namespace NandWatch.Core.Tests;

public class RstReportTests
{
    /// <summary>A STORAGE_DEVICE_DESCRIPTOR with an NVMe bus and the given strings (invented).</summary>
    internal static byte[] Descriptor(string product, string serial, string revision)
    {
        var b = new byte[256];
        void Put(int offsetField, int at, string text)
        {
            BitConverter.GetBytes(at).CopyTo(b, offsetField);
            System.Text.Encoding.ASCII.GetBytes(text).CopyTo(b, at);
        }
        Put(16, 64, product);
        Put(20, 128, revision);
        Put(24, 160, serial);
        BitConverter.GetBytes(StorageBus.Nvme).CopyTo(b, 28);
        return b;
    }

    internal static string Json(byte[] page, bool ok = true, uint[]? completion = null, DateTimeOffset? at = null) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["version"] = 1,
        ["startedUtc"] = (at ?? new DateTimeOffset(2026, 10, 25, 6, 37, 0, TimeSpan.Zero)).ToString("O"),
        ["finishedUtc"] = (at ?? new DateTimeOffset(2026, 10, 25, 6, 37, 0, TimeSpan.Zero)).AddSeconds(1).ToString("O"),
        ["drives"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["disk"] = 0, ["descriptor"] = Convert.ToBase64String(Descriptor("Example TLC 2TB", "0000_0000_0000_0021.", "FW2")),
                ["port"] = 0, ["path"] = 0, ["target"] = 0, ["lun"] = 0, ["ok"] = ok, ["error"] = ok ? 0 : 1, ["detail"] = ok ? null : "Incorrect function.",
                ["srbReturnCode"] = 0u, ["completion"] = completion ?? new uint[4], ["page"] = ok ? Convert.ToBase64String(page) : null,
            },
        },
    });

    [Fact]
    public void A_report_round_trips_and_its_page_decodes()
    {
        var report = RstReport.Parse(Json(Pages.Health()));
        Assert.NotNull(report);
        var d = Assert.Single(report.Drives);
        Assert.Equal("Example TLC 2TB", d.Device!.Product);
        Assert.Equal("0000_0000_0000_0021.", d.Device.Serial);
        Assert.NotNull(d.Health);
        Assert.Null(RstReport.Implausible(d));
    }

    [Fact]
    public void A_page_the_miniport_never_filled_is_refused()
    {
        var d = RstReport.Parse(Json(new byte[512]))!.Drives[0];
        Assert.Equal("the page is empty", RstReport.Implausible(d));
    }

    [Fact]
    public void The_completion_dwords_decide_nothing()
    {
        // The owner's Micron: a valid page, with DW3 reading as status 0x80 (2026-10-24).
        var d = RstReport.Parse(Json(Pages.Health(), completion: [0, 0, 0, 0x01000000]))!.Drives[0];
        Assert.Null(RstReport.Implausible(d));
        Assert.Equal("00000000 00000000 00000000 01000000", d.CompletionText);
    }

    [Fact]
    public void The_cross_check_passes_the_same_drive_and_names_what_differs()
    {
        var own = HealthLog.Parse(Pages.Health());
        Assert.Null(RstReport.CrossCheck(own, own));
        var other = HealthLog.Parse(Pages.Health(p => { p[3] = 50; Pages.U128(p, 128, 99_999); }));   // spare, power-on hours
        var why = RstReport.CrossCheck(other, own);
        Assert.Contains("spare", why);
        Assert.Contains("power-on hours", why);
    }

    [Fact]
    public void Something_else_is_not_a_report() => Assert.Null(RstReport.Parse("{\"version\":2}"));
}

public class MergedHealthTests
{
    [Fact]
    public void The_newest_health_log_is_kept_between_daily_reads()
    {
        using var db = new TestDb();
        var drive = new DriveIdentity("Example TLC 2TB", "0000_0000_0000_0021.", "FW2");
        var id = HealthDb.UpsertDrive(db.Conn, drive, DateTimeOffset.UnixEpoch);
        var t0 = new DateTimeOffset(2026, 10, 25, 6, 37, 0, TimeSpan.Zero);
        var run = HealthDb.StartRun(db.Conn, "test", t0);
        HealthDb.InsertReading(db.Conn, run, id, new DriveReading
        {
            Drive = drive, DiskNumber = 0, At = t0, Source = DriveReading.RstSource,
            Health = HealthLog.Parse(Pages.Health()), HealthPage = Pages.Health(),
        }, TimeZoneInfo.Utc);
        // Hourly readings after it carry only the temperature.
        HealthDb.InsertReading(db.Conn, run, id, new DriveReading { Drive = drive, DiskNumber = 0, At = t0.AddMinutes(30), SensorTemperatureC = 47 }, TimeZoneInfo.Utc);

        var latest = HealthQueries.Latest(db.Conn, id)!;
        Assert.Equal(t0.AddMinutes(30), latest.At);
        Assert.Equal((short)47, latest.SensorTemperatureC);
        Assert.True(latest.HasHealth);
        Assert.Equal(t0, latest.HealthAt);
        Assert.Equal("rst", latest.HealthSource);
        Assert.NotNull(latest.AvailableSpare);
    }
}
