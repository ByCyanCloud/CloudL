namespace CloudL.Domain.Shared.Exceptions;

/// <summary>
/// 权限不足业务异常：映射为 HTTP 403。
/// <para>与 <see cref="UnauthorizedBusinessException"/>（401：未认证 / 凭据错误）区分开：
/// 已登录但无权访问时应返回 403，否则客户端会误以为需要重新登录。</para>
/// </summary>
public class ForbiddenBusinessException : BusinessException
{
    /// <summary>使用默认业务错误码 <c>4030</c>。</summary>
    public ForbiddenBusinessException(string message)
        : base(CloudL.Domain.Shared.Constants.ErrorCodes.Forbidden, message)
    {
    }

    /// <summary>指定业务错误码。</summary>
    public ForbiddenBusinessException(int businessCode, string message)
        : base(businessCode, message)
    {
    }
}
