namespace CloudL.Domain.Shared.Exceptions;

/// <summary>
/// 请求过于频繁 / 被临时锁定业务异常：映射为 HTTP 429。
/// <para>与限流中间件返回的 429 呼应：中间件按 IP 拦（业务码 4290），
/// 本异常用于"账号连续登录失败被临时锁定"这类业务判断（业务码 4291）。</para>
/// </summary>
public class TooManyRequestsException : BusinessException
{
    /// <summary>使用默认业务错误码 <c>4290</c>。</summary>
    public TooManyRequestsException(string message)
        : base(CloudL.Domain.Shared.Constants.ErrorCodes.TooManyRequests, message)
    {
    }

    /// <summary>指定业务错误码（账号锁定请用 <c>ErrorCodes.AccountLocked</c>）。</summary>
    public TooManyRequestsException(int businessCode, string message)
        : base(businessCode, message)
    {
    }
}
