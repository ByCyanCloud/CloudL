using CloudL.Domain.Shared.Constants;
using System.Reflection;
using CloudL.Domain.DomainEvents;
using CloudL.Domain.Entities;
using CloudL.Domain.Repositories;
using System;
using System.Linq;
using CloudL.Domain.Shared.Persistence;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace CloudL.SqlSugar;

/// <summary>
/// CloudL.SqlSugar 的依赖注入入口。
/// </summary>
public static class SqlSugarModule
{
    /// <summary>
    /// 注册 SqlSugar 客户端（<see cref="ISqlSugarClient"/>，线程安全）与持久化标记。
    /// </summary>
    /// <remarks>
    /// <para>仓储与工作单元随后由本包注册（见后续版本）。</para>
    /// <para><strong>互斥</strong>：若同一应用已经注册了 EF Core 持久化（<c>AddCloudLEntityFrameworkCore</c>），
    /// 本方法会<strong>立即抛异常</strong> —— 一个项目只允许一套 ORM，理由见 CONTRACT.md。</para>
    /// </remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">配置委托。</param>
    public static IServiceCollection AddCloudLSqlSugar(
        this IServiceCollection services,
        Action<SqlSugarOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        EnsureNoOtherPersistenceProvider(services);

        var options = new SqlSugarOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("SqlSugar 连接字符串未配置（SqlSugarOptions.ConnectionString）。");

        var dbType = ParseDbType(options.DbType);

        services.AddSingleton(new PersistenceProvider(PersistenceProviderKind.SqlSugar));

        // SqlSugarScope 是线程安全实现，适合注册为单例；连接由 SqlSugar 自行管理
        services.AddSingleton<ISqlSugarClient>(_ => new SqlSugarScope(
            new ConnectionConfig
            {
                ConnectionString = options.ConnectionString,
                DbType = dbType,
                IsAutoCloseConnection = true,
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    // 框架基类里有不属于数据库的成员（如 BaseEntity.DomainEvents）。
                    // EF 侧靠 BaseEntityConfiguration 的 Ignore 排除，SqlSugar 没有那份配置，
                    // 因此必须在这里显式忽略 —— 否则会被当成列写入，运行时直接抛
                    // "No mapping exists from object type ... IDomainEvent"。
                    EntityService = (property, column) =>
                    {
                        if (column.IsIgnore)
                            return;

                        // 忽略规则来自 CloudL.Core 的中立声明 NotPersistedAttribute，
                        // 不依赖任何具体 ORM 的配置，避免只有某个 ORM 才知道要忽略
                        // 字符串列：未显式指定长度时，套用与 EF 侧（ApplyDefaultStringConventions）相同的默认长度，
                        // 直接引用同一个常量，避免两边漂移
                        if (property.PropertyType == typeof(string) && column.DataType is null)
                        {
                            column.DataType = dbType switch
                            {
                                DbType.PostgreSQL => $"character varying({AppConstants.DefaultStringMaxLength})",
                                DbType.SqlServer => $"nvarchar({AppConstants.DefaultStringMaxLength})",
                                DbType.Dm => $"VARCHAR({AppConstants.DefaultStringMaxLength})",
                                DbType.Oracle => $"VARCHAR2({AppConstants.DefaultStringMaxLength})",
                                DbType.MySql => $"varchar({AppConstants.DefaultStringMaxLength})",
                                _ => "TEXT"
                            };
                        }

                        // 主键：框架把主键定义在 Entity<TKey>.Id 上（EF 侧由 BaseEntityConfiguration 声明），
                        // SqlSugar 看不到那份配置 —— 不标出来，Updateable(entity)/Deleteable(entity) 会因为
                        // 既没有主键也没有条件 而直接抛 SqlSugarException。
                        if (property.Name == "Id"
                            && property.DeclaringType is { IsGenericType: true } declaring
                            && declaring.GetGenericTypeDefinition() == typeof(Entity<>))
                        {
                            column.IsPrimarykey = true;
                        }

                        // 框架不存储时区：时间列必须是不带时区的类型（EF 侧由 ConfigureConventions 做同样的事）
                        if (property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?))
                        {
                            column.DataType ??= dbType switch
                            {
                                DbType.PostgreSQL => "timestamp without time zone",
                                DbType.SqlServer => "datetime2",
                                DbType.Dm => "TIMESTAMP",
                                DbType.Oracle => "TIMESTAMP",
                                DbType.MySql => "datetime",
                                _ => "TEXT"
                            };
                        }

                        if (property.IsDefined(typeof(NotPersistedAttribute), inherit: true))
                        {
                            column.IsIgnore = true;
                        }
                    }
                }            },
            client =>
            {
                if (options.EnableSqlLog)
                    client.Aop.OnLogExecuting = (sql, _) => Console.WriteLine(sql);
            }));

        // 通用仓储与工作单元（与 EF 版同一份契约）
        services.AddScoped(typeof(IRepository<,>), typeof(SqlSugarRepository<,>));
        services.AddScoped(typeof(ISqlSugarRepository<,>), typeof(SqlSugarRepository<,>));
        services.AddScoped<SqlSugarUnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SqlSugarUnitOfWork>());

        return services;
    }

    /// <summary>
    /// 互斥检查：发现已注册<strong>其它</strong>持久化实现时立即失败（启动即失败）。
    /// </summary>
    private static void EnsureNoOtherPersistenceProvider(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(PersistenceProvider));

        if (existing?.ImplementationInstance is PersistenceProvider provider
            && provider.Kind != PersistenceProviderKind.SqlSugar)
        {
            throw new InvalidOperationException(
                $"本应用已启用持久化实现：{provider.Kind}。" +
                "同一个项目只允许一套 ORM（否则会出现两套审计/事务/领域事件/迁移语义，导致数据不一致）。" +
                "请二选一：CloudL.EntityFrameworkCore 或 CloudL.SqlSugar。");
        }
    }

    /// <summary>解析数据库类型（含常见别名），无法识别时给出可操作的错误。</summary>
    private static DbType ParseDbType(string value)
    {
        var normalized = value?.Trim() switch
        {
            null or "" => "Dm",
            var v when v.Equals("Npgsql", StringComparison.OrdinalIgnoreCase) => "PostgreSQL",
            var v when v.Equals("MSSQL", StringComparison.OrdinalIgnoreCase) => "SqlServer",
            var v when v.Equals("MySQL", StringComparison.OrdinalIgnoreCase) => "MySql",
            var v => v
        };

        if (Enum.TryParse<DbType>(normalized, ignoreCase: true, out var dbType))
            return dbType;

        throw new InvalidOperationException(
            $"SqlSugarOptions.DbType 无法识别：'{value}'。请使用 SqlSugar.DbType 的枚举名，" +
            "例如 Dm（达梦）、PostgreSQL、SqlServer、MySql、Sqlite、Oracle。");
    }
}
