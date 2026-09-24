namespace CloudL.Domain.Shared.Exceptions;

/// <summary>
/// 资源不存在业务异常：映射为 HTTP 404。
/// <para>用它而不是直接抛 <c>KeyNotFoundException</c>：后者是 BCL 异常，
/// 字典取值失败、库内部实现也会抛它 —— 那是服务端 bug，不该被当成 404 返回给调用方。</para>
/// </summary>
public class NotFoundException : BusinessException
{
    /// <summary>使用默认业务错误码 <c>4040</c>。</summary>
    public NotFoundException(string message)
        : base(CloudL.Domain.Shared.Constants.ErrorCodes.NotFound, message)
    {
    }

    /// <summary>指定业务错误码。</summary>
    public NotFoundException(int businessCode, string message)
        : base(businessCode, message)
    {
    }
}
