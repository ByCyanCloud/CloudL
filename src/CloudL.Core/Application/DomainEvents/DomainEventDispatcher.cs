using System.Collections.Concurrent;
using System.Reflection;
using CloudL.Domain.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CloudL.Application.DomainEvents;

/// <summary>
/// 领域事件分发器：按事件的具体类型解析已注册的 <see cref="IDomainEventHandler{TDomainEvent}"/> 并逐个执行。
/// 处理器需在 DI 中注册（可用 <c>AddDomainEventHandler&lt;TEvent, THandler&gt;()</c>）。
/// </summary>
public class DomainEventDispatcher : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> HandlerInvokers = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DomainEventDispatcher> _logger;

    public DomainEventDispatcher(
        IServiceProvider serviceProvider,
        ILogger<DomainEventDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (var domainEvent in domainEvents)
        {
            var eventType = domainEvent.GetType();
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            var invoker = HandlerInvokers.GetOrAdd(handlerType, static type => type.GetMethod("HandleAsync")!);

            var handledCount = 0;
            foreach (var handler in _serviceProvider.GetServices(handlerType))
            {
                if (handler is null)
                    continue;

                if (invoker.Invoke(handler, [domainEvent, cancellationToken]) is Task task)
                    await task.ConfigureAwait(false);

                handledCount++;
            }

            if (handledCount == 0)
            {
                _logger.LogDebug(
                    "领域事件无处理器: {EventType} @ {OccurredAt}",
                    eventType.Name, domainEvent.OccurredAt);
            }
            else
            {
                _logger.LogInformation(
                    "领域事件已分发: {EventType} → {HandlerCount} 个处理器",
                    eventType.Name, handledCount);
            }
        }
    }
}
