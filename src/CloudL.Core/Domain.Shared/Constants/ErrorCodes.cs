namespace CloudL.Domain.Shared.Constants;

/// <summary>
/// 业务错误码常量。
/// 四位编码：前三位沿用 HTTP 状态码语义，第四位为同分类下的细分序号。
/// </summary>
public static class ErrorCodes
{
    /// <summary>成功（HTTP 200）。</summary>
    public const int Success = 2000;

    /// <summary>请求参数验证失败（HTTP 400）。</summary>
    public const int ValidationError = 4000;

    /// <summary>业务规则校验不通过（HTTP 400）。</summary>
    public const int InvalidInput = 4001;

    /// <summary>未认证：未提供 Token，或 Token 无效 / 过期（HTTP 401）。</summary>
    public const int Unauthorized = 4010;

    /// <summary>账号或密码错误（HTTP 401）。</summary>
    public const int CredentialsError = 4011;

    /// <summary>权限不足（HTTP 403）。</summary>
    public const int Forbidden = 4030;

    /// <summary>资源不存在（HTTP 404）。</summary>
    public const int NotFound = 4040;

    /// <summary>资源状态冲突 / 并发冲突（HTTP 409）。</summary>
    public const int Conflict = 4090;

    /// <summary>服务器内部错误（HTTP 500）。</summary>
    public const int ServerError = 5000;

    /// <summary>下游 / 第三方服务调用失败（HTTP 502）。</summary>
    public const int BadGateway = 5020;

    /// <summary>下游 / 第三方服务超时（HTTP 504）。</summary>
    public const int GatewayTimeout = 5040;
}