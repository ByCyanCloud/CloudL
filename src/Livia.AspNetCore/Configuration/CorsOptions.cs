namespace Livia.AspNetCore.Configuration;

/// <summary>
/// CORS 配置，对应配置节 <c>Cors</c>。
/// 安全默认：<see cref="AllowAnyOrigin"/> 为 <c>false</c>，白名单为空时不放行任何来源。
/// </summary>
public class CorsOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Cors";

    /// <summary>默认策略名称。</summary>
    public const string DefaultPolicyName = "DefaultCors";

    /// <summary>允许的来源白名单。非空时优先生效。</summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>白名单为空时是否允许任意来源。</summary>
    public bool AllowAnyOrigin { get; set; }

    /// <summary>是否允许携带凭据。与 <see cref="AllowAnyOrigin"/> 同时为真时不生效。</summary>
    public bool AllowCredentials { get; set; } = true;

    /// <summary>需要暴露给浏览器的响应头。</summary>
    public string[] ExposedHeaders { get; set; } = ["Content-Disposition"];
}