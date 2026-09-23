using System.ComponentModel.DataAnnotations;

namespace Livia.AspNetCore.Configuration;

/// <summary>
/// JWT 配置，对应配置节 <c>Jwt</c>。配置缺失或非法时应用会在<strong>启动阶段</strong>即报错。
/// </summary>
public class JwtOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Jwt";

    /// <summary>签名密钥。生产环境请通过环境变量或密钥管理服务注入，不要提交到仓库。</summary>
    [Required(ErrorMessage = "Jwt:SecretKey 未配置")]
    [MinLength(32, ErrorMessage = "Jwt:SecretKey 长度至少需要 32 个字符")]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>签发者。</summary>
    [Required(ErrorMessage = "Jwt:Issuer 未配置")]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>受众。</summary>
    [Required(ErrorMessage = "Jwt:Audience 未配置")]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Access Token 过期时间（小时）。</summary>
    [Range(1, 720, ErrorMessage = "Jwt:ExpirationHours 必须在 1 到 720 之间")]
    public int ExpirationHours { get; set; } = 8;

    /// <summary>Refresh Token 过期时间（天）。</summary>
    [Range(1, 365, ErrorMessage = "Jwt:RefreshTokenExpirationDays 必须在 1 到 365 之间")]
    public int RefreshTokenExpirationDays { get; set; } = 7;

    /// <summary>校验时钟偏移容差（秒）。</summary>
    [Range(0, 300, ErrorMessage = "Jwt:ClockSkewSeconds 必须在 0 到 300 之间")]
    public int ClockSkewSeconds { get; set; } = 60;
}