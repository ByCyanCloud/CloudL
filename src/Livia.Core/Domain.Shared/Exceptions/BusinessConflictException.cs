namespace Livia.Domain.Shared.Exceptions;

/// <summary>
/// 业务冲突异常：唯一约束冲突、资源状态冲突等，映射为 HTTP 409。
/// </summary>
public class BusinessConflictException : Exception
{
    public BusinessConflictException(string message)
        : base(message)
    {
    }

    public BusinessConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}