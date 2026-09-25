using CloudL.Domain.Shared.Time;
using Xunit;

namespace CloudL.UnitTests;

/// <summary>
/// 时间口径契约：三种 <c>Clock</c> 都产出<strong>不带时区</strong>的墙上钟时间（Kind=Unspecified）。
/// </summary>
public class TimeOptionsTests
{
    [Fact]
    public void Default_ShouldBeUtcWallClock()
    {
        var now = new TimeOptions().Now();

        Assert.Equal(DateTimeKind.Unspecified, now.Kind);
        Assert.True((DateTime.UtcNow - now).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void FixedOffset_ShouldBeUtcPlusOffset()
    {
        var now = new TimeOptions { Clock = "+08:00" }.Now();

        Assert.Equal(DateTimeKind.Unspecified, now.Kind);
        Assert.True((DateTime.UtcNow.AddHours(8) - now).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Local_ShouldMatchServerLocalTime()
    {
        var now = new TimeOptions { Clock = "Local" }.Now();

        Assert.Equal(DateTimeKind.Unspecified, now.Kind);
        Assert.True((DateTime.Now - now).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ToWallClock_ShouldConvertExplicitOffsetToConfiguredClock()
    {
        // 北京时间 10:00（带 +08:00 偏移）
        var value = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.FromHours(8));

        Assert.Equal(new DateTime(2026, 9, 25, 10, 0, 0), new TimeOptions { Clock = "+08:00" }.ToWallClock(value));
        Assert.Equal(new DateTime(2026, 9, 25, 2, 0, 0), new TimeOptions { Clock = "Utc" }.ToWallClock(value));
    }

    [Fact]
    public void UnknownClock_ShouldThrowWithActionableMessage()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new TimeOptions { Clock = "Mars/Phobos" }.Now());

        Assert.Contains("Time:Clock", exception.Message, StringComparison.Ordinal);
    }
}
