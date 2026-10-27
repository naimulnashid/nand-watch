using System.Globalization;

namespace NandWatch.Core.View;

/// <summary>
/// Every figure as the app shows it. Bytes are DECIMAL (TB = 10^12), the unit
/// endurance ratings use; HWiNFO and others show binary TiB as "TBytes", about
/// 10% lower for the same bytes, so the unit is always printed.
/// </summary>
public static class Format
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    /// <summary>A byte count as (number, unit): "25.31" "TB", "84.4" "GB", "512" "MB".</summary>
    public static (string Value, string Unit) Bytes(double bytes)
    {
        var abs = Math.Abs(bytes);
        if (abs >= 1e12) return ((bytes / 1e12).ToString(abs >= 1e14 ? "N0" : "N2", En), "TB");
        if (abs >= 1e9) return ((bytes / 1e9).ToString(abs >= 1e11 ? "N0" : "N1", En), "GB");
        if (abs >= 1e6) return ((bytes / 1e6).ToString("N0", En), "MB");
        if (abs >= 1e3) return ((bytes / 1e3).ToString("N0", En), "KB");
        return (bytes.ToString("N0", En), "B");
    }

    public static string BytesText(double bytes)
    {
        var (v, u) = Bytes(bytes);
        return $"{v} {u}";
    }

    public static string Count(long n) => n.ToString("N0", En);

    /// <summary>An endurance rating: "1,200 TBW".</summary>
    public static string Tbw(decimal tbw) => tbw.ToString("N0", En) + " TBW";

    /// <summary>A chart axis's terabytes: "1,200 TB", "0.5 TB".</summary>
    public static string TbTick(double tb) => tb.ToString("#,0.##", En) + " TB";

    public static string Percent(double p, int decimals = 1) => p.ToString("N" + decimals, En) + "%";

    public static string Temperature(short? c) => c is null ? "not reported" : $"{c} °C";

    /// <summary>A local time the way the app writes it: 24-hour, the date only when it is not today.</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        if (local.Date == today) return local.ToString("HH:mm", En);
        if (local.Date == today.AddDays(-1)) return "yesterday " + local.ToString("HH:mm", En);
        return local.ToString("yyyy-MM-dd HH:mm", En);
    }

    /// <summary>How long ago, coarsely: "just now", "12 min ago", "3 h ago", "4 days ago".</summary>
    public static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var span = now - at;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(2)) return $"{(int)span.TotalHours} h ago";
        return $"{(int)span.TotalDays} days ago";
    }

    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", En);

    /// <summary>A chart axis's day: "Oct 24".</summary>
    public static string DayShort(DateOnly d) => d.ToString("MMM d", En);

    /// <summary>A tooltip's day: "Sat 2026-10-24".</summary>
    public static string DayLong(DateOnly d) => d.ToString("ddd yyyy-MM-dd", En);

    /// <summary>A moment in a tooltip: "Sat 2026-10-24 14:07", local.</summary>
    public static string Moment(DateTimeOffset at, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString("ddd yyyy-MM-dd HH:mm", En);
}
