namespace CloudL.Domain.DomainEvents;

/// <summary>
/// 领域事件分发器。由 <c>FrameworkDbContext</c> 在 SaveChanges 成功后调用。
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>分发领域事件。</summary>
    Task DispatchAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default);
}
