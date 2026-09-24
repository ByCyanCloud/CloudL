using CloudL.Domain.Repositories;
using CloudL.EntityFrameworkCore.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CloudL.EntityFrameworkCore;

/// <summary>
/// CloudL.EntityFrameworkCore 的依赖注入入口。
/// </summary>
public static class EntityFrameworkCoreModule
{
    /// <summary>
    /// 注册 DbContext（审计 / 乐观锁 / 领域事件）、通用仓储与工作单元。
    /// </summary>
    /// <remarks>
    /// <para>数据库提供程序由调用方显式指定，因此核心包<strong>不依赖任何具体数据库</strong>，
    /// 消费方也不会被拖入用不到的提供程序依赖：</para>
    /// <code>
    /// // PostgreSQL：引用 CloudL.EntityFrameworkCore.PostgreSql 包
    /// services.AddCloudLEntityFrameworkCore&lt;AppDbContext&gt;(options =&gt;
    ///     options.UseCloudLPostgreSql(configuration.GetRequiredConnectionString()));
    ///
    /// // SQL Server：引用 CloudL.EntityFrameworkCore.SqlServer 包
    /// services.AddCloudLEntityFrameworkCore&lt;AppDbContext&gt;(options =&gt;
    ///     options.UseCloudLSqlServer(configuration.GetRequiredConnectionString()));
    /// </code>
    /// </remarks>
    /// <typeparam name="TContext">业务 DbContext 类型。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="configureDatabase">数据库提供程序与连接串的配置委托。</param>
    public static IServiceCollection AddCloudLEntityFrameworkCore<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDatabase)
        where TContext : FrameworkDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureDatabase);

        services.AddDbContext<TContext>((_, options) => configureDatabase(options));

        // 让基类可被注入：仓储与工作单元只依赖 FrameworkDbContext，而不耦合具体业务 Context
        services.AddScoped<FrameworkDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<TContext>());

        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.AddScoped(typeof(IRepository<,>), typeof(EfCoreRepository<,>));

        return services;
    }
}
