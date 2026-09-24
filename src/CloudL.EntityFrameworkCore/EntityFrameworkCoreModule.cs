using CloudL.Domain.Repositories;
using CloudL.EntityFrameworkCore.Extensions;
using CloudL.EntityFrameworkCore.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CloudL.EntityFrameworkCore;

/// <summary>
/// CloudL.EntityFrameworkCore 的依赖注入入口。
/// </summary>
public static class EntityFrameworkCoreModule
{
    /// <summary>
    /// 按配置注册 DbContext 与仓储。
    /// 读取 <c>ConnectionStrings:Default</c> 与 <c>Database:Provider</c>（默认 postgresql）。
    /// </summary>
    /// <typeparam name="TContext">业务 DbContext 类型。</typeparam>
    public static IServiceCollection AddCloudLEntityFrameworkCore<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : FrameworkDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("未配置数据库连接串：ConnectionStrings:Default");

        var provider = configuration.GetValue<string>("Database:Provider")
            ?? DbContextOptionsExtensions.PostgreSqlProvider;

        return services.AddCloudLEntityFrameworkCore<TContext>(connectionString, provider);
    }

    /// <summary>
    /// 按连接串与提供程序注册 DbContext 与仓储。
    /// </summary>
    /// <typeparam name="TContext">业务 DbContext 类型。</typeparam>
    public static IServiceCollection AddCloudLEntityFrameworkCore<TContext>(
        this IServiceCollection services,
        string connectionString,
        string provider)
        where TContext : FrameworkDbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<TContext>(options =>
            options.UseCloudLDatabaseProvider(connectionString, provider));

        // 让基类可被注入：仓储与工作单元只依赖 FrameworkDbContext，而不耦合具体业务 Context
        services.AddScoped<FrameworkDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<TContext>());

        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.AddScoped(typeof(IRepository<,>), typeof(EfCoreRepository<,>));

        return services;
    }
}