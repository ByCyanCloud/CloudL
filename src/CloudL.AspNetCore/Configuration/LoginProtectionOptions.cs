using System.ComponentModel.DataAnnotations;

namespace CloudL.AspNetCore.Configuration;

/// <summary>
/// 登录失败计数与临时锁定配置（配置节 <c>LoginProtection</c>）。
/// </summary>
public sealed class LoginProtectionOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "LoginProtection";

    /// <summary>触发锁定所需的连续失败次数。</summary>
    [Range(1, 1000)]
    public int MaxFailures { get; set; } = 5;

    /// <summary>失败计数的统计窗口（秒）：超过该窗口的失败不再累计。</summary>
    [Range(1, 86400)]
    public int FailureWindowSeconds { get; set; } = 300;

    /// <summary>触发后锁定多长时间（秒）。</summary>
    [Range(1, 86400)]
    public int LockoutSeconds { get; set; } = 60;
}
