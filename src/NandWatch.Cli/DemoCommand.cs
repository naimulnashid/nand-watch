using System.Globalization;
using NandWatch.Core;
using NandWatch.Core.Data;
using NandWatch.Core.Nvme;

namespace NandWatch.Cli;

/// <summary>
/// <c>nandwatch demo-data &lt;dir&gt;</c>: an invented history for screenshots
/// and for trying the app without touching the real one. Two invented drives,
/// read hourly while the "PC" is on, with days off, one stretch where nothing
/// collected, a hot spell, a free-space clean-up and a few failed runs, so
/// every state the pages draw appears. Seeded, so it is the same every time
/// for the same <c>NANDWATCH_DEMO_NOW</c>.
/// </summary>
internal static class DemoCommand
{
    private sealed class Drive(string model, string id, decimal ratedTbw, decimal writtenTb, decimal readTb, long poh, long cycles, long unsafeShutdowns,
        double meanGbPerDay, double baseTempC, short warningC, string letter, double totalBytes, double freeStart, double freeEnd, double vendorFactor)
    {
        public DriveIdentity Identity { get; } = new(model, id, "DEMO1.0");
        public decimal RatedTbw { get; } = ratedTbw;
        public UInt128 Written = (UInt128)(writtenTb * 1_953_125);
        public UInt128 Read = (UInt128)(readTb * 1_953_125);
        public UInt128 ReadCommands, WriteCommands, BusyMinutes;
        public UInt128 PowerOnHours = (UInt128)poh, PowerCycles = (UInt128)cycles, UnsafeShutdowns = (UInt128)unsafeShutdowns;
        public uint WarningMinutes;
        public double MeanGbPerDay { get; } = meanGbPerDay;
        public double BaseTempC { get; } = baseTempC;
        public short WarningC { get; } = warningC;
        public string Letter { get; } = letter;
        public double TotalBytes { get; } = totalBytes;
        public double FreeStart { get; } = freeStart;
        public double FreeEnd { get; } = freeEnd;

        /// <summary>The drive's own wear estimate trails bytes written, as the real Micron's does (21% against 32.6%).</summary>
        public byte PercentUsed => (byte)Math.Min(255, (int)(HealthLog.Terabytes(Written) / RatedTbw * 100 * (decimal)vendorFactor));
        public long Id;
    }

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Usage: nandwatch demo-data <dir> [--days N]");
        var root = Path.GetFullPath(args[0]);
        var days = Program.Arg(args, "days") is { } n ? int.Parse(n, CultureInfo.InvariantCulture) : 180;
        var dataDir = Path.Combine(root, "data");
        var localDir = Path.Combine(root, "local");
        var dbPath = AppPaths.DatabasePath(dataDir);
        if (File.Exists(dbPath)) throw new InvalidOperationException($"{dbPath} exists already. Delete {root} first: an invented history must never be mixed into another.");

        var now = Environment.GetEnvironmentVariable("NANDWATCH_DEMO_NOW") is { Length: > 0 } pinned
            ? DateTimeOffset.Parse(pinned, CultureInfo.InvariantCulture) : DateTimeOffset.Now;
        var zone = TimeZoneInfo.Local;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var rng = new Random(2026);

        Drive[] drives =
        [
            new("Example QLC 512GB", "DEMO_0000_0000_0001.", 150, 34.0m, 38.0m, 11_800, 1_180, 44, 70, 37, 87, "C:", 510.8e9, 0.24, 0.17, 0.65),
            new("Example TLC 2TB", "DEMO_0000_0000_0002.", 1200, 28.0m, 32.0m, 3_200, 1_150, 28, 55, 45, 85, "D:", 2000.4e9, 0.12, 0.06, 0.5),
        ];

        Directory.CreateDirectory(localDir);
        using var conn = HealthDb.Open(dbPath, allowSystemDrive: true);
        using (var tx = conn.BeginTransaction())
        {
            foreach (var d in drives)
            {
                d.Id = HealthDb.UpsertDrive(conn, d.Identity, now.AddDays(-days));
                HealthDb.SetDriveSettings(conn, d.Id, d.RatedTbw, null);
            }

            // Three days the PC ran but nothing collected; one hot afternoon; a clean-up of C:.
            var uncollected = (From: days - 120, To: days - 118);
            var hotDay = days - 30;
            var cleanupDay = days - 50;
            var unsafeDays = new HashSet<int> { days - 140, days - 75, days - 9 };

            for (var day = 0; day < days; day++)
            {
                var date = today.AddDays(day - days + 1);
                var isToday = date == today;
                if (!isToday && rng.NextDouble() < 0.08) continue;   // off all day: no readings, counters still

                var first = 8 + rng.Next(3);
                var last = 21 + rng.Next(3);
                var hours = Enumerable.Range(first, last - first + 1).ToList();
                var targets = drives.Select(d => d.MeanGbPerDay * Math.Exp(0.45 * Gaussian(rng)) * (rng.NextDouble() < 0.05 ? 4 : 1)).ToArray();
                foreach (var d in drives) d.PowerCycles++;
                if (unsafeDays.Contains(day)) foreach (var d in drives) d.UnsafeShutdowns++;

                foreach (var hour in hours)
                {
                    var local = date.ToDateTime(new TimeOnly(hour, 7));
                    var at = new DateTimeOffset(local, zone.GetUtcOffset(local));
                    if (at > now) break;

                    for (var i = 0; i < drives.Length; i++)
                    {
                        var d = drives[i];
                        var gb = targets[i] / hours.Count * (0.4 + 1.2 * rng.NextDouble());
                        var units = (UInt128)(gb * 1e9 / HealthLog.BytesPerDataUnit);
                        var readUnits = (UInt128)((double)units * (0.9 + 0.6 * rng.NextDouble()));
                        d.Written += units;
                        d.Read += readUnits;
                        d.WriteCommands += units * 512_000 / 32_768;
                        d.ReadCommands += readUnits * 512_000 / 16_384;
                        d.BusyMinutes += (UInt128)rng.Next(1, 5);
                        d.PowerOnHours++;
                    }

                    if (day >= uncollected.From && day <= uncollected.To) continue;
                    var trigger = hour == first ? "logon" : "hourly";
                    var run = HealthDb.StartRun(conn, trigger, at);
                    if (rng.NextDouble() < 0.004)
                    {
                        HealthDb.FinishRun(conn, run, "failed", 0, null, 40, "Invented failure: the drive did not answer.", at.AddMilliseconds(40));
                        continue;
                    }

                    var daylight = Math.Sin((hour - 8) / 14.0 * Math.PI);
                    foreach (var d in drives)
                    {
                        var hot = day == hotDay && d.Letter == "D:" && hour is 15 or 16;
                        var temp = (short)Math.Round(hot ? d.WarningC + 1 : d.BaseTempC + 7 * daylight + 2 * Gaussian(rng));
                        if (hot) d.WarningMinutes += 6;
                        var health = new HealthLog
                        {
                            CriticalWarning = CriticalWarning.None,
                            CompositeTemperatureK = (ushort)(temp + 273),
                            AvailableSpare = 100,
                            AvailableSpareThreshold = 5,
                            PercentageUsed = d.PercentUsed,
                            EnduranceGroupCriticalWarning = 0,
                            DataUnitsRead = d.Read,
                            DataUnitsWritten = d.Written,
                            HostReadCommands = d.ReadCommands,
                            HostWriteCommands = d.WriteCommands,
                            ControllerBusyMinutes = d.BusyMinutes,
                            PowerCycles = d.PowerCycles,
                            PowerOnHours = d.PowerOnHours,
                            UnsafeShutdowns = d.UnsafeShutdowns,
                            MediaErrors = 0,
                            ErrorLogEntries = 0,
                            WarningTemperatureMinutes = d.WarningMinutes,
                            CriticalTemperatureMinutes = 0,
                            TemperatureSensorsK = [(ushort)(temp + 273), 0, 0, 0, 0, 0, 0, 0],
                            ThermalLimit1Transitions = 0,
                            ThermalLimit2Transitions = 0,
                            ThermalLimit1Seconds = 0,
                            ThermalLimit2Seconds = 0,
                        };
                        HealthDb.InsertReading(conn, run, d.Id, new DriveReading
                        {
                            Drive = d.Identity,
                            DiskNumber = d.Letter == "C:" ? 1 : 0,
                            At = at,
                            Health = health,
                            HealthPage = health.ToPage(),
                            SensorTemperatureC = temp,
                            WarningTemperatureC = d.WarningC,
                        }, zone);

                        var progress = (double)day / days;
                        var free = d.FreeStart + (d.FreeEnd - d.FreeStart) * progress + (d.Letter == "C:" && day >= cleanupDay ? 0.06 : 0) + 0.003 * Gaussian(rng);
                        HealthDb.InsertVolume(conn, run, d.Id, new VolumeReading(d.Letter, d.Letter == "C:" ? 1 : 0, (long)d.TotalBytes, (long)(d.TotalBytes * free)), at, zone);
                    }
                    HealthDb.FinishRun(conn, run, "ok", drives.Length, hour == first ? "ok" : null, 30 + rng.Next(40), null, at.AddMilliseconds(60));
                }
            }
            // Windows' side of the story (the Events page): its record of the
            // unsafe shutdowns above, missing one (power cut while asleep), and a
            // handful of storage events, some on a disk that is no longer attached.
            long record = 1;
            DateTimeOffset At(int day, int hour)
            {
                var local = today.AddDays(day - days + 1).ToDateTime(new TimeOnly(hour, 9));
                return new DateTimeOffset(local, zone.GetUtcOffset(local));
            }
            foreach (var day in unsafeDays.Where(d => d != days - 75))
            {
                HealthDb.InsertEvent(conn, new SystemEvent(At(day, 9), Core.View.SystemEvents.KernelPower, 41, 1, null, record++), zone);
                HealthDb.InsertEvent(conn, new SystemEvent(At(day, 9), Core.View.SystemEvents.EventLog, 6008, 2, null, record++), zone);
            }
            foreach (var (day, provider, id, level, disk) in new[]
            {
                (days - 60, "disk", 153, 3, 1), (days - 41, "disk", 153, 3, 1), (days - 41, "stornvme", 129, 3, 1),
                (days - 20, "disk", 7, 2, 2), (days - 20, "disk", 7, 2, 2), (days - 19, "disk", 7, 2, 2), (days - 19, "Ntfs", 140, 3, 2),
            })
                HealthDb.InsertEvent(conn, new SystemEvent(At(day, 14), provider, id, level, disk, record++), zone);

            HealthDb.SetMeta(conn, "last_backup_utc", HealthDb.Iso(now));
            HealthDb.SetMeta(conn, "demo", "true");
            tx.Commit();
        }

        var readings = HealthDb.CountReadings(conn);
        Console.WriteLine($"Invented {days} days ending {today:yyyy-MM-dd}: {readings:N0} readings of {drives.Length} drives in {dbPath}");
        Console.WriteLine("Point the app and the CLI at it, in PowerShell (set BOTH, or the real folders are used):");
        Console.WriteLine($"  $env:{AppPaths.DataDirVariable} = '{dataDir}'");
        Console.WriteLine($"  $env:{AppPaths.LocalDirVariable} = '{localDir}'");
        return 0;
    }

    /// <summary>A standard normal draw (Box-Muller), for writes and temperatures that wander plausibly.</summary>
    private static double Gaussian(Random rng) => Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
}
