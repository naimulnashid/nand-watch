using NandWatch.Core.View;

namespace NandWatch.Core.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(25_312_345_678_900d, "25.31", "TB")]
    [InlineData(150e12, "150", "TB")]
    [InlineData(84_400_000_000d, "84.4", "GB")]
    [InlineData(510_823_231_488d, "511", "GB")]
    [InlineData(76_000d, "76", "KB")]
    public void Bytes_are_decimal_with_their_unit(double bytes, string value, string unit)
    {
        Assert.Equal((value, unit), Format.Bytes(bytes));
    }

    [Fact]
    public void Ratings_and_terabyte_ticks_group_thousands()
    {
        Assert.Equal("1,200 TBW", Format.Tbw(1200m));
        Assert.Equal("150 TBW", Format.Tbw(150m));
        Assert.Equal("1,200 TB", Format.TbTick(1200));
        Assert.Equal("0.5 TB", Format.TbTick(0.5));
    }

    [Fact]
    public void Times_drop_the_date_today_and_say_yesterday()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+6", TimeSpan.FromHours(6), "UTC+6", "UTC+6");
        var now = new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);   // 16:00 local
        Assert.Equal("13:48", Format.When(now.AddHours(-2).AddMinutes(-12), now, zone));
        Assert.Equal("yesterday 23:00", Format.When(now.AddHours(-17), now, zone));
        Assert.Equal("2026-10-20 16:00", Format.When(now.AddDays(-4), now, zone));
    }

    [Fact]
    public void Ago_is_coarse()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        Assert.Equal("just now", Format.Ago(now.AddSeconds(-20), now));
        Assert.Equal("12 min ago", Format.Ago(now.AddMinutes(-12), now));
        Assert.Equal("3 h ago", Format.Ago(now.AddHours(-3), now));
        Assert.Equal("4 days ago", Format.Ago(now.AddDays(-4), now));
    }
}
