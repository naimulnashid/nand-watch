using NandWatch.Core.Nvme;

namespace NandWatch.Core.Query;

public enum AlertLevel
{
    /// <summary>Amber: wear past a step, a drive at its warning temperature.</summary>
    Warning,

    /// <summary>Red: something broke. A warning bit, spare at its threshold, media errors, critical temperature.</summary>
    Critical,
}

/// <summary>One thing a reading showed that the owner should hear about. <c>Kind</c> is stable, for the history.</summary>
public sealed record Alert(string Kind, AlertLevel Level, string Title, string Body);

/// <summary>
/// What changed between a drive's previous reading and this one that is
/// worth a notification. Every rule fires on a CHANGE (a bit newly set, a
/// counter that grew, a threshold crossed), so a drive in a bad state is
/// announced once, not every hour; its panel keeps saying it.
/// </summary>
/// <remarks>
/// <para><b>The first reading of a drive raises nothing</b>: it is the
/// baseline. A drive that arrives with media errors is red on the Overview
/// at once; a notification would only repeat it.</para>
/// <para>Red means something broke, as everywhere in the app (a warning bit, spare at or below its
/// threshold, new media errors, critical-temperature time that grew); wear
/// crossing 80, 90 or 100% and reaching the warning temperature are amber.</para>
/// </remarks>
public static class Alerts
{
    public static readonly int[] WearSteps = [80, 90, 100];

    /// <param name="usedBefore">Written over rated, in percent, at the previous reading; null without a rating.</param>
    /// <param name="newMediaEntries">Error-log entries for an I/O queue or a media status first seen in this reading.</param>
    public static IReadOnlyList<Alert> Compare(string drive, LatestReading? before, LatestReading after,
        decimal? usedBefore = null, decimal? usedAfter = null, int newMediaEntries = 0)
    {
        var alerts = new List<Alert>();
        if (before is null) return alerts;

        if (after.HasHealth && before.HasHealth)
        {
            var added = (CriticalWarning)(after.CriticalWarning ?? 0) & ~(CriticalWarning)(before.CriticalWarning ?? 0);
            if (added != CriticalWarning.None)
                alerts.Add(new("critical-warning", AlertLevel.Critical, $"{drive} reports a critical warning",
                    $"{Describe(added)}. Back up what matters on it now."));

            if (after.AvailableSpare <= after.SpareThreshold && before.AvailableSpare > before.SpareThreshold)
                alerts.Add(new("spare", AlertLevel.Critical, $"{drive} is running out of spare blocks",
                    $"Spare is at {after.AvailableSpare}%, at or below the {after.SpareThreshold}% the drive warns at. Back up what matters on it now."));

            if (after.MediaErrors > before.MediaErrors)
            {
                var n = after.MediaErrors - before.MediaErrors;
                alerts.Add(new("media-errors", AlertLevel.Critical, $"{drive}: {n} new media {(n == 1 ? "error" : "errors")}",
                    "Data the drive could not read back. Back up what matters on it now."));
            }

            if (after.CriticalTemperatureMinutes > before.CriticalTemperatureMinutes)
                alerts.Add(new("critical-temperature", AlertLevel.Critical, $"{drive} reached its critical temperature",
                    $"It spent {after.CriticalTemperatureMinutes - before.CriticalTemperatureMinutes} more minutes above it. Check the airflow around it."));
            else if (after.WarningTemperatureMinutes > before.WarningTemperatureMinutes)
                alerts.Add(new("warning-temperature", AlertLevel.Warning, $"{drive} ran hot",
                    $"It spent {after.WarningTemperatureMinutes - before.WarningTemperatureMinutes} more minutes above its warning temperature."));

            foreach (var step in WearSteps)
                if (before.PercentageUsed < step && after.PercentageUsed >= step)
                    alerts.Add(new($"wear-vendor-{step}", AlertLevel.Warning, $"{drive}: its own wear estimate reached {step}%",
                        $"The drive reports {after.PercentageUsed}% of its life used."));
        }
        else if (Hot(after) && !Hot(before))
        {
            // No health log: Windows' sensor reading is the only temperature signal.
            alerts.Add(new("warning-temperature", AlertLevel.Warning, $"{drive} is at its warning temperature",
                $"{after.SensorTemperatureC} °C; the drive warns at {after.WarningTemperatureC} °C."));
        }

        if (usedBefore is { } ub && usedAfter is { } ua)
            foreach (var step in WearSteps)
                if (ub < step && ua >= step)
                    alerts.Add(new($"wear-written-{step}", AlertLevel.Warning, $"{drive} has written {step}% of its rating",
                        step >= 100 ? "It has reached its rated endurance (TBW): the warranty's limit, not a failure point. Keep backups current."
                                    : $"{ua:N1}% of its rated endurance (TBW) is written."));

        if (newMediaEntries > 0)
            alerts.Add(new("error-log", AlertLevel.Critical, $"{drive} logged {newMediaEntries} I/O or media {(newMediaEntries == 1 ? "error" : "errors")}",
                "Its own error log recorded a failed read or write. Back up what matters on it now."));
        return alerts;
    }

    private static bool Hot(LatestReading r) => r.SensorTemperatureC is { } t && r.WarningTemperatureC is { } w && t >= w;

    /// <summary>The critical-warning bits in words.</summary>
    public static string Describe(CriticalWarning bits)
    {
        var parts = new List<string>();
        if (bits.HasFlag(CriticalWarning.SpareBelowThreshold)) parts.Add("spare below its threshold");
        if (bits.HasFlag(CriticalWarning.Temperature)) parts.Add("temperature outside its limits");
        if (bits.HasFlag(CriticalWarning.ReliabilityDegraded)) parts.Add("reliability degraded by media errors");
        if (bits.HasFlag(CriticalWarning.ReadOnly)) parts.Add("the drive has made itself read-only");
        if (bits.HasFlag(CriticalWarning.VolatileBackupFailed)) parts.Add("its volatile memory backup failed");
        if (bits.HasFlag(CriticalWarning.PersistentMemoryReadOnly)) parts.Add("its persistent memory region is read-only");
        var text = parts.Count == 0 ? "an unknown warning bit" : string.Join("; ", parts);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
