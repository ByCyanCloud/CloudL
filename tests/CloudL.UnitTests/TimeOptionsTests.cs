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
        var value = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.FromHours(8));

        Assert.Equal(new DateTime(2026, 9, 25, 10, 0, 0), new TimeOptions { Clock = "+08:00" }.ToWallClock(value));
        Assert.Equal(new DateTime(2026, 9, 25, 2, 0, 0), new TimeOptions { Clock = "Utc" }.ToWallClock(value));
    }

    // ---------- A-1：DateTime 重载（三种 Kind） ----------

    [Fact]
    public void ToWallClock_Unspecified_ShouldReturnAsIs()
    {
        // 库里读回来的就是这种：它本身已是墙上钟，绝不能再用 new DateTimeOffset(...) 套一层偏移
        var value = new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Unspecified);

        var result = new TimeOptions { Clock = "+08:00" }.ToWallClock(value);

        Assert.Equal(value, result);
        Assert.Equal(DateTimeKind.Unspecified, result.Kind);
    }

    [Fact]
    public void ToWallClock_Utc_ShouldConvertToConfiguredWallClock()
    {
        var value = new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

        var beijing = new TimeOptions { Clock = "+08:00" }.ToWallClock(value);
        var utc = new TimeOptions { Clock = "Utc" }.ToWallClock(value);

        Assert.Equal(new DateTime(2026, 9, 25, 18, 0, 0), beijing);
        Assert.Equal(DateTimeKind.Unspecified, beijing.Kind);
        Assert.Equal(new DateTime(2026, 9, 25, 10, 0, 0), utc);
    }

    [Fact]
    public void ToWallClock_Local_ShouldConvertViaLocalTime()
    {
        var value = new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Local);

        var result = new TimeOptions { Clock = "+08:00" }.ToWallClock(value);

        // 与实现同源的算法，不依赖测试机器的时区：LocalDateTime 会换算到本机时区
        var expected = new DateTimeOffset(value).UtcDateTime.AddHours(8);
        Assert.Equal(expected, result);
        Assert.Equal(DateTimeKind.Unspecified, result.Kind);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ToWallClock_AnyKind_ShouldNeverReturnUtcKind(DateTimeKind kind)
    {
        var value = new DateTime(2026, 9, 25, 10, 0, 0, kind);

        var result = new TimeOptions { Clock = "+08:00" }.ToWallClock(value);

        Assert.Equal(DateTimeKind.Unspecified, result.Kind);
    }

    [Fact]
    public void ToWallClock_Nullable_ShouldHandleNull()
    {
        var options = new TimeOptions { Clock = "+08:00" };

        Assert.Null(options.ToWallClock((DateTime?)null));
        Assert.Equal(
            new DateTime(2026, 9, 25, 10, 0, 0),
            options.ToWallClock((DateTime?)new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void UnknownClock_ShouldThrowWithActionableMessage()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new TimeOptions { Clock = "Mars/Phobos" }.Now());

        Assert.Contains("Time:Clock", exception.Message, StringComparison.Ordinal);
    }
}
