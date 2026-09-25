namespace CloudL.Domain.DomainEvents;

/// <summary>
/// 领域事件接口。所有领域事件都应实现此接口。
/// </summary>
public interface IDomainEvent
{
    /// <summary>事件发生时间（UTC）。</summary>
    DateTimeOffset OccurredAt { get; }
}
