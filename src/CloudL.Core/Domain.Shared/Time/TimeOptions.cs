using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace CloudL.Domain.Shared.Time;

/// <summary>
/// 时间口径配置（配置节 <c>Time</c>）。
/// </summary>
/// <remarks>
/// <para>本框架按要求<strong>不存储时区</strong>：数据库列一律"不带时区"，库里存的就是墙上钟时间（wall clock）。
/// <see cref="Clock"/> 决定这个墙上钟取自哪里：</para>
/// <list type="bullet">
///   <item><c>Utc</c>（默认）：取 UTC；</item>
///   <item>固定偏移（如 <c>+08:00</c>）：取"UTC + 偏移"，<strong>与服务器时区无关</strong>；</item>
///   <item><c>Local</c>：取服务器本地时区（等价于 <c>DateTime.Now</c>）。</item>
/// </list>
/// <para>⚠️ 选 <c>Local</c> 时，若运行环境时区不一致（云端容器默认 UTC、开发机 +8），同一时刻会写出不同的
/// 墙上钟，数据里会混存两种时间。要可控就必须把运行环境时区钉死（容器设 <c>TZ=Asia/Shanghai</c>）。</para>
/// </remarks>
public sealed class TimeOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Time";

    /// <summary>时间口径：<c>Utc</c>（默认）、固定偏移（如 <c>+08:00</c>）或 <c>Local</c>。</summary>
    [Required]
    public string Clock { get; set; } = "Utc";

    /// <summary>当前的墙上钟时间（<c>Kind=Unspecified</c>，不带时区标识）。</summary>
    public DateTime Now()
    {
        var value = Clock?.Trim();

        if (string.IsNullOrEmpty(value) || string.Equals(value, "Utc", StringComparison.OrdinalIgnoreCase))
            return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        if (string.Equals(value, "Local", StringComparison.OrdinalIgnoreCase))
            return DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);

        return DateTime.SpecifyKind(DateTime.UtcNow + ParseOffset(value), DateTimeKind.Unspecified);
    }

    /// <summary>把带偏移的时间换算成本口径的墙上钟时间。</summary>
    public DateTime ToWallClock(DateTimeOffset value)
    {
        var current = Clock?.Trim();

        if (string.IsNullOrEmpty(current) || string.Equals(current, "Utc", StringComparison.OrdinalIgnoreCase))
            return DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Unspecified);

        if (string.Equals(current, "Local", StringComparison.OrdinalIgnoreCase))
            return DateTime.SpecifyKind(value.LocalDateTime, DateTimeKind.Unspecified);

        return DateTime.SpecifyKind(value.UtcDateTime + ParseOffset(current), DateTimeKind.Unspecified);
    }

    /// <summary>
    /// 解析固定偏移。
    /// </summary>
    /// <remarks>
    /// 注意：<c>TimeSpan.TryParse</c> <strong>不接受前导 <c>+</c></strong>（只接受 <c>-</c>），
    /// 而配置里最常见的写法恰恰是 <c>+08:00</c> —— 所以这里先剥掉 <c>+</c> 再解析，否则会直接抛异常。
    /// </remarks>
    private static TimeSpan ParseOffset(string value)
    {
        var normalized = value.StartsWith('+') ? value[1..] : value;

        if (TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out var offset))
            return offset;

        throw new InvalidOperationException(
            $"配置 Time:Clock 无法识别：'{value}'。支持 Utc、Local 或固定偏移（如 +08:00）。");
    }
}

/// <summary>
/// 当前生效的时间口径。
/// </summary>
/// <remarks>
/// 静态持有是刻意的：这样 <c>FrameworkDbContext</c> 不必为注入时钟而改构造函数，
/// 也就不会破坏业务侧已有的派生上下文。
/// </remarks>
public static class CloudLTime
{
    private static TimeOptions _current = new();

    /// <summary>当前生效的配置。</summary>
    public static TimeOptions Current => _current;

    /// <summary>按配置初始化（由 <c>AddCloudLAspNetCore</c> 调用）。</summary>
    public static void Configure(TimeOptions? options) => _current = options ?? new TimeOptions();

    /// <summary>当前的墙上钟时间。</summary>
    public static DateTime Now() => _current.Now();

    /// <summary>把带偏移的时间换算成当前口径的墙上钟时间。</summary>
    public static DateTime ToWallClock(DateTimeOffset value) => _current.ToWallClock(value);
}
