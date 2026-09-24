using CloudL.Application.DomainEvents;
using CloudL.Domain.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CloudL.UnitTests;

/// <summary>
/// 领域事件分发器测试：按事件具体类型解析处理器，无处理器时不抛异常。
/// </summary>
public class DomainEventDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_ShouldInvokeRegisteredHandler()
    {
        var handler = new TestEventHandler();
        await using var provider = BuildProvider(services =>
            services.AddSingleton<IDomainEventHandler<TestEvent>>(handler));

        var dispatcher = CreateDispatcher(provider);

        await dispatcher.DispatchAsync([new TestEvent(DateTimeOffset.UtcNow)]);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal("payload", handler.LastPayload);
    }

    [Fact]
    public async Task DispatchAsync_ShouldNotThrow_WhenNoHandlerRegistered()
    {
        await using var provider = BuildProvider();
        var dispatcher = CreateDispatcher(provider);

        var exception = await Record.ExceptionAsync(() =>
            dispatcher.DispatchAsync([new TestEvent(DateTimeOffset.UtcNow)]));

        Assert.Null(exception);
    }

    [Fact]
    public async Task DispatchAsync_ShouldOnlyInvokeHandlersMatchingEventType()
    {
        var matchingHandler = new TestEventHandler();
        var otherHandler = new OtherEventHandler();

        await using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IDomainEventHandler<TestEvent>>(matchingHandler);
            services.AddSingleton<IDomainEventHandler<OtherEvent>>(otherHandler);
        });

        var dispatcher = CreateDispatcher(provider);

        await dispatcher.DispatchAsync([new TestEvent(DateTimeOffset.UtcNow)]);

        Assert.Equal(1, matchingHandler.CallCount);
        Assert.Equal(0, otherHandler.CallCount);
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static DomainEventDispatcher CreateDispatcher(IServiceProvider provider) =>
        new(provider, provider.GetRequiredService<ILogger<DomainEventDispatcher>>());

    private sealed record TestEvent(DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed record OtherEvent(DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed class TestEventHandler : IDomainEventHandler<TestEvent>
    {
        public int CallCount { get; private set; }

        public string? LastPayload { get; private set; }

        public Task HandleAsync(TestEvent domainEvent, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPayload = "payload";
            return Task.CompletedTask;
        }
    }

    private sealed class OtherEventHandler : IDomainEventHandler<OtherEvent>
    {
        public int CallCount { get; private set; }

        public Task HandleAsync(OtherEvent domainEvent, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
