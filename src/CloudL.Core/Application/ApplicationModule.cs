using System.Reflection;
using CloudL.Application.DomainEvents;
using CloudL.Application.Mapping;
using CloudL.Domain.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudL.Application;

/// <summary>
/// CloudL.Core 的依赖注入入口。
/// </summary>
public static class ApplicationModule
{
    /// <summary>
    /// 注册核心服务：领域事件分发器与 Mapster 全局约定。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="mappingAssemblies">包含 Mapster <c>IRegister</c> 实现的业务程序集（可选）。</param>
    public static IServiceCollection AddCloudLCore(
        this IServiceCollection services,
        params Assembly[] mappingAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        MapsterFrameworkConfig.Configure(mappingAssemblies);

        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        return services;
    }

    /// <summary>
    /// 注册一个领域事件处理器。
    /// </summary>
    /// <typeparam name="TDomainEvent">领域事件类型。</typeparam>
    /// <typeparam name="TDomainEventHandler">处理器实现类型。</typeparam>
    public static IServiceCollection AddDomainEventHandler<TDomainEvent, TDomainEventHandler>(
        this IServiceCollection services)
        where TDomainEvent : IDomainEvent
        where TDomainEventHandler : class, IDomainEventHandler<TDomainEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IDomainEventHandler<TDomainEvent>, TDomainEventHandler>();

        return services;
    }
}
