namespace CloudL.AspNetCore.Configuration;

/// <summary>
/// 日志敏感信息开关，对应配置节 <c>Logging</c> 下的 <c>RevealSensitiveDataInLogs</c>：
/// <list type="bullet">
///   <item><c>true</c>（<strong>默认</strong>）：<c>[Auth]</c> 请求头、<c>[Body]</c> 请求体、请求日志摘要里的 query
///         中的敏感值都<strong>按原文</strong>写进日志（不再出现 <c>***</c>）；</item>
///   <item><c>false</c>：恢复两层脱敏（按键名 + 按值的形态，见
///         <see cref="CloudL.AspNetCore.Logging.SensitiveDataRedactor"/>）。</item>
/// </list>
/// <para>⚠️ <strong>生产环境请显式设为 <c>false</c></strong>，否则日志会包含明文凭据（口令、令牌、密钥），
/// 任何能读日志的人都等于拿到了这些凭据。</para>
/// <para>为什么复用标准日志节 <c>Logging</c>：开关与"日志怎么记"在同一节里，一眼能看到；
/// 该节原有的 <c>LogLevel</c> 不受影响（绑定只取 <c>RevealSensitiveDataInLogs</c> 一个键）。</para>
/// </summary>
public class SensitiveDataLoggingOptions
{
    /// <summary>配置节名称（标准日志节）。</summary>
    public const string SectionName = "Logging";

    /// <summary>完整配置键：<c>Logging:RevealSensitiveDataInLogs</c>。</summary>
    public const string RevealSensitiveDataInLogsKey = "RevealSensitiveDataInLogs";

    /// <summary>
    /// 该键的默认值：<c>true</c>。即<strong>不配置该键时也走明文</strong>（框架明确定下的默认行为）。
    /// </summary>
    public const bool DefaultRevealSensitiveDataInLogs = true;

    /// <summary>
    /// 是否在日志中明文显示敏感信息。<c>true</c> = 明文，<c>false</c> = 脱敏。
    /// </summary>
    public bool RevealSensitiveDataInLogs { get; set; } = DefaultRevealSensitiveDataInLogs;
}
