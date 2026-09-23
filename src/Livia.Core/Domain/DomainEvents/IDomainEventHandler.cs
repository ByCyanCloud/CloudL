namespace Livia.Domain.DomainEvents;

/// <summary>
/// 领域事件处理器。业务侧实现后在 DI 中注册即可被自动分发：
/// <code>services.AddScoped&lt;IDomainEventHandler&lt;OrderCreatedEvent&gt;, SendOrderMailHandler&gt;();</code>
/// </summary>
/// <typeparam name="TDomainEvent">领域事件类型。</typeparam>
public interface IDomainEventHandler<in TDomainEvent>
    where TDomainEvent : IDomainEvent
{
    /// <summary>处理领域事件。</summary>
    Task HandleAsync(TDomainEvent domainEvent, CancellationToken cancellationToken = default);
}