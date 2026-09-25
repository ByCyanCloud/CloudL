// ============================================================================
// 本文件由 CloudL 模板生成，属于「框架装配」部分。
// 升级 CloudL.* 包不会更新本文件（模板是复制，不是依赖）。
// 需要同步模板改进时，在框架仓库执行：
//     pwsh ./build/compare-template.ps1 -ProjectPath <你的项目根目录>
// ============================================================================
using CloudL.EntityFrameworkCore;
using CloudL.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
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
    /// </summary>
    /// <remarks>
    /// <para>数据库提供程序在这里<strong>显式指定</strong>（而不是靠配置里的名字字符串），
    /// 因此每个项目引用哪个 provider 包、用哪个数据库，在代码里一目了然：
    /// 换数据库只需换包 + 改下面这一行，框架核心包不受影响。</para>
    /// <code>
    /// // PostgreSQL（默认，见 TemplateProject.EntityFrameworkCore.csproj 的引用）
    /// options.UseCloudLPostgreSql(connectionString);
    ///
    /// // SQL Server：把包换成 CloudL.EntityFrameworkCore.SqlServer 后
    /// options.UseCloudLSqlServer(connectionString);
    /// </code>
    /// </remarks>
    /// <typeparam name="TContext">业务 DbContext 类型。</typeparam>
    public static IServiceCollection AddProjectEntityFrameworkCore<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : FrameworkDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetRequiredConnectionString();

        // 框架：DbContext（审计/乐观锁/领域事件）+ 通用仓储 IRepository<,> + 工作单元
        services.AddCloudLEntityFrameworkCore<TContext>(options =>
            options.UseCloudLPostgreSql(connectionString));

        // 业务：专用仓储
        services.AddScoped<IUserRepository, UserRepository>();

        return services;
    }
}
