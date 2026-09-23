namespace Livia.AspNetCore.Configuration;

/// <summary>
/// 单个第三方 HTTP 客户端的配置。配置节名即客户端名：<c>HttpClients:{name}</c>。
/// </summary>
public class HttpClientEntryOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "HttpClients";

    /// <summary>基地址。为空时调用方需传入完整 URL。</summary>
    public string? BaseUrl { get; set; }

    /// <summary>请求超时（秒）。</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>未显式传 token 时，是否透传当前请求的 Authorization 头（网关场景）。</summary>
    public bool ForwardAuthorization { get; set; }
}