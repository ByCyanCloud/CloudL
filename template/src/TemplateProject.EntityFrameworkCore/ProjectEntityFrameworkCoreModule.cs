using Livia.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TemplateProject.Domain.Repositories;
using TemplateProject.EntityFrameworkCore.Repositories;

namespace TemplateProject.EntityFrameworkCore;

/// <summary>
/// EntityFrameworkCore 层依赖注入注册。
/// </summary>
public static class ProjectEntityFrameworkCoreModule
{
    /// <summary>
    /// 注册 DbContext、框架通用仓储与业务专用仓储。
    /// 连接串与提供程序来自配置：<c>ConnectionStrings:Default</c> 与 <c>Database:Provider</c>。
    /// </summary>
    /// <typeparam name="TContext">业务 DbContext 类型。</typeparam>
    public static IServiceCollection AddProjectEntityFrameworkCore<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : FrameworkDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 框架：DbContext（审计/乐观锁/领域事件）+ 通用仓储 IRepository<,> + 工作单元
        services.AddLiviaEntityFrameworkCore<TContext>(configuration);

        // 业务：专用仓储
        services.AddScoped<IUserRepository, UserRepository>();

        return services;
    }
}
