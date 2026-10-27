using NandWatch.Core.Nvme;

namespace NandWatch.Core.Query;

/// <summary>A write rate, and the stretch of history it was measured over.</summary>
public sealed record WriteRate(decimal TbPerDay, double SpanDays);

/// <summary>
/// The two life figures, side by side and never blended, and the projection.
/// </summary>
public sealed record LifeEstimate
{
    public required decimal WrittenTb { get; init; }
    public decimal? RatedTbw { get; init; }

    /// <summary>Written over rated: fine-grained, ours.</summary>
    public decimal? UsedByWritesPercent { get; init; }

    /// <summary>The drive's own Percentage Used: whole steps, may pass 100.</summary>
    public int? VendorPercentUsed { get; init; }

    public WriteRate? Rate30 { get; init; }
    public WriteRate? Rate90 { get; init; }

    /// <summary>When the rating is reached at each rate; null without a rate, or beyond a century.</summary>
    public DateOnly? End30 { get; init; }
    public DateOnly? End90 { get; init; }
}

/// <summary>
/// When a drive reaches its rated TBW at its recent pace. A range from two
/// windows, not one date: a single figure would claim a precision the habits
/// behind it do not have. TBW is the warranty's limit, not a failure point.
/// </summary>
public static class Projection
{
    /// <summary>Less history than this gives no rate: a week of holiday or of installs is not a habit.</summary>
    public static readonly TimeSpan MinimumHistory = TimeSpan.FromDays(14);

    /// <summary>
    /// The calendar rate over the last <paramref name="window"/>, from the
    /// reading at or before its start (else the earliest) to the newest. Off
    /// days count: they are part of how fast the drive is used up.
    /// </summary>
    public static WriteRate? Rate(IReadOnlyList<CounterPoint> points, DateTimeOffset now, TimeSpan window)
    {
        if (points.Count < 2) return null;
        var sorted = points.OrderBy(p => p.At).ToList();
        var startIndex = sorted.FindLastIndex(p => p.At <= now - window);
        if (startIndex < 0) startIndex = 0;
        var span = sorted[^1].At - sorted[startIndex].At;
        if (span < MinimumHistory) return null;
        long units = 0;
        for (var i = startIndex + 1; i < sorted.Count; i++) units += Math.Max(0, sorted[i].Value - sorted[i - 1].Value);
        return new WriteRate(HealthLog.Terabytes((UInt128)units) / (decimal)span.TotalDays, span.TotalDays);
    }

    public static LifeEstimate Estimate(IReadOnlyList<CounterPoint> written, int? vendorPercentUsed, decimal? ratedTbw, DateTimeOffset now, TimeZoneInfo zone)
    {
        var newest = written.OrderBy(p => p.At).LastOrDefault();
        var writtenTb = newest is null ? 0 : HealthLog.Terabytes((UInt128)newest.Value);
        var rate30 = Rate(written, now, TimeSpan.FromDays(30));
        var rate90 = Rate(written, now, TimeSpan.FromDays(90));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        return new LifeEstimate
        {
            WrittenTb = writtenTb,
            RatedTbw = ratedTbw,
            UsedByWritesPercent = ratedTbw is > 0 ? writtenTb / ratedTbw * 100 : null,
            VendorPercentUsed = vendorPercentUsed,
            Rate30 = rate30,
            Rate90 = rate90,
            End30 = End(rate30),
            End90 = End(rate90),
        };

        DateOnly? End(WriteRate? rate)
        {
            if (ratedTbw is not > 0 || rate is not { TbPerDay: > 0 }) return null;
            var remaining = ratedTbw.Value - writtenTb;
            if (remaining <= 0) return today;
            var days = remaining / rate.TbPerDay;
            return days > 36_500 ? null : today.AddDays((int)Math.Ceiling(days));
        }
    }
}
