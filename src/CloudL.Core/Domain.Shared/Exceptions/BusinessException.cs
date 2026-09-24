namespace CloudL.Domain.Shared.Exceptions;

/// <summary>
/// 通用业务异常：预期内的业务错误（如业务规则不满足、输入不合法）。
/// 由全局异常中间件映射为 HTTP 400，并在响应中透传 <see cref="BusinessCode"/>。
/// </summary>
public class BusinessException : Exception
{
    /// <summary>业务错误码，取值见 <see cref="Constants.ErrorCodes"/>。</summary>
    public int BusinessCode { get; }

    /// <summary>可选的、可安全暴露给客户端的附加信息。</summary>
    public string? Details { get; }

    public BusinessException(int businessCode, string message, string? details = null)
        : base(message)
    {
        BusinessCode = businessCode;
        Details = details;
    }

    public BusinessException(int businessCode, string message, Exception innerException)
        : base(message, innerException)
    {
        BusinessCode = businessCode;
    }
}