namespace CloudL.AspNetCore.Logging;

/// <summary>
/// 日志敏感数据策略：由配置 <c>Logging:RevealSensitiveDataInLogs</c> 决定
/// <strong>日志明文显示敏感信息</strong>（默认）还是<strong>走脱敏</strong>。
/// </summary>
/// <remarks>
/// <para><strong>为什么要有这个抽象</strong>：这是一处开关、多条渲染路径共用的决策
/// （<c>[Auth]</c> 头、<c>[Body]</c> 请求体、请求日志摘要里的 query，外加启动横幅），
/// 把决策收进一个可注入的只读对象后：
/// 调用点只依赖接口（显式依赖，不靠静态可变字段）；测试里可以替换成任意一种模式；
/// 也不会退化成"某处改了全局开关、另一处还在用旧值"的隐藏状态。</para>
/// <para>实现见 <see cref="SensitiveDataLogPolicy"/>，脱敏本身仍由
/// <see cref="SensitiveDataRedactor"/>（纯函数）负责。</para>
/// </remarks>
public interface ISensitiveDataLogPolicy
{
    /// <summary>是否明文显示敏感信息：<c>true</c> = 日志按原文记录，<c>false</c> = 脱敏。</summary>
    bool RevealSensitiveData { get; }

    /// <summary>
    /// 渲染块状日志里的 <c>[Auth]</c> 段。
    /// 空值（缺失的头、只有空白）在两种模式下都渲染为 <see cref="SensitiveDataRedactor.NonePlaceholder"/>。
    /// </summary>
    string FormatAuthorizationHeader(string? authorizationHeader);

    /// <summary>
    /// 渲染块状日志里的 <c>[Body]</c> 段。
    /// 空体（含只有空白）在两种模式下都渲染为 <see cref="SensitiveDataRedactor.NonePlaceholder"/>。
    /// </summary>
    string FormatBody(string? body);

    /// <summary>
    /// 渲染请求日志摘要里的 query 段（对应输出模板里的 <c>{RedactedQuery}</c> 属性）。
    /// 空 query 在两种模式下都渲染为空串。
    /// </summary>
    string FormatQueryString(string? queryString);
}
