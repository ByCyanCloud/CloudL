namespace CloudL.Domain.Shared.Exceptions;

/// <summary>
/// 未认证业务异常：如登录时账号或密码错误，映射为 HTTP 401。
/// 继承 <see cref="BusinessException"/> 以保留业务错误码透传能力。
/// </summary>
public class UnauthorizedBusinessException : BusinessException
{
    public UnauthorizedBusinessException(int businessCode, string message)
        : base(businessCode, message)
    {
    }
}