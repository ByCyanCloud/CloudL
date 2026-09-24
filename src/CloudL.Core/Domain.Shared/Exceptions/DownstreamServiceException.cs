namespace CloudL.Domain.Shared.Exceptions;

/// <summary>
/// 下游 / 第三方服务调用失败，映射为 HTTP 502。
/// <see cref="Details"/> 仅用于日志与排查，<strong>不会</strong>返回给客户端，避免泄漏第三方响应内容。
/// </summary>
public class DownstreamServiceException : Exception
{
    /// <summary>业务错误码，取值见 <see cref="Constants.ErrorCodes"/>。</summary>
    public int BusinessCode { get; }

    /// <summary>面向开发者的诊断信息（第三方状态码、响应摘要等），不对外暴露。</summary>
    public string? Details { get; }

    public DownstreamServiceException(int businessCode, string message, string? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        BusinessCode = businessCode;
        Details = details;
    }
}

/// <summary>
/// 下游 / 第三方服务调用超时，映射为 HTTP 504。
/// </summary>
public sealed class DownstreamTimeoutException : DownstreamServiceException
{
    public DownstreamTimeoutException(int businessCode, string message, string? details = null, Exception? innerException = null)
        : base(businessCode, message, details, innerException)
    {
    }
}