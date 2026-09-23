using Livia.Application;
using Microsoft.Extensions.DependencyInjection;
using TemplateProject.Application.Contracts.IServices;
using TemplateProject.Application.Handlers;
using TemplateProject.Application.Services;
using TemplateProject.Domain.Events;

namespace TemplateProject.Application;

/// <summary>
/// Application 层依赖注入注册。
/// </summary>
public static class ProjectApplicationModule
{
    /// <summary>
    /// 注册 Application 层服务与领域事件处理器。
    /// </summary>
    public static IServiceCollection AddProjectApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IUserService, UserService>();

        // 框架提供的扩展方法：把领域事件处理器注册到 IDomainEventDispatcher 可解析的位置
        services.AddDomainEventHandler<UserRegisteredEvent, UserRegisteredEventHandler>();

        return services;
    }
}
