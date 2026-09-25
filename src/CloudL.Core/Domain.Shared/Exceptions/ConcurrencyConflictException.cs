namespace CloudL.Domain.Shared.Exceptions;

/// <summary>
/// 并发冲突异常（乐观锁失败），映射为 HTTP 409。
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
        : base("数据已被其他用户修改，请刷新后重试")
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
