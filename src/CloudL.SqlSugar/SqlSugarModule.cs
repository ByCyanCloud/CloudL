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
                        // 注意：SqlSugar 会把 DataType 预填成它自己的默认值（DateTime -> TIMESTAMP），
                        // 所以不能以 DataType is null 判断用户是否显式配置过 —— 那样下面两条规则永远不会生效
                        // （2026-09-30 由按 DbType 的列约定测试发现：五种库的 DataType 全是 TIMESTAMP）。
                        // 改为：属性上显式标了 ColumnDataType 才跳过。
                        var explicitType = property.GetCustomAttribute<SugarColumn>()?.ColumnDataType;

                        if (string.IsNullOrWhiteSpace(explicitType))
                        {
                            if (property.PropertyType == typeof(string))
                            {
                                column.DataType = dbType switch
                                {
                                    global::SqlSugar.DbType.PostgreSQL => $"character varying({AppConstants.DefaultStringMaxLength})",
                                    global::SqlSugar.DbType.SqlServer => $"nvarchar({AppConstants.DefaultStringMaxLength})",
                                    global::SqlSugar.DbType.Dm => $"VARCHAR({AppConstants.DefaultStringMaxLength})",
                                    global::SqlSugar.DbType.Oracle => $"VARCHAR2({AppConstants.DefaultStringMaxLength})",
                                    global::SqlSugar.DbType.MySql => $"varchar({AppConstants.DefaultStringMaxLength})",
                                    _ => "TEXT"
                                };
                            }
                            else if (property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?))
                            {
                                // 框架不存储时区：时间列必须是不带时区的类型（与 EF 侧 ConfigureConventions 同一意图）
                                column.DataType = dbType switch
                                {
                                    global::SqlSugar.DbType.PostgreSQL => "timestamp without time zone",
                                    global::SqlSugar.DbType.SqlServer => "datetime2",
                                    global::SqlSugar.DbType.Dm => "TIMESTAMP",
                                    global::SqlSugar.DbType.Oracle => "TIMESTAMP",
                                    global::SqlSugar.DbType.MySql => "datetime",
                                    _ => "TEXT"
                                };
                            }
                        }

                        // 可空性：显式设置 DataType 会丢掉它（曾把可空列建成 NOT NULL）；按 CLR 类型补回，与 EF 默认一致
                        if (property.PropertyType.IsClass || Nullable.GetUnderlyingType(property.PropertyType) is not null)
                        {
                            column.IsNullable = true;
                        }

                        // 主键：框架把主键定义在 Entity<TKey>.Id 上（EF 侧由 BaseEntityConfiguration 声明），
                        // SqlSugar 看不到那份配置 —— 不标出来，Updateable(entity)/Deleteable(entity) 会直接抛异常
                        if (property.Name == "Id"
                            && property.DeclaringType is { IsGenericType: true } declaring
                            && declaring.GetGenericTypeDefinition() == typeof(Entity<>))
                        {
                            column.IsPrimarykey = true;
                        }

                        // 忽略规则来自 CloudL.Core 的中立声明（NotPersistedAttribute），不依赖任何具体 ORM 的配置
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

        // 把配置暴露给宿主服务（InitTables 需要它）
        services.AddSingleton(options);

        if (options.EnableInitTables)
        {
            // 开启却不给实体清单 = 什么都不会发生（此前正是这种静默失效），所以启动即失败
            if (options.InitTablesEntityTypes.Count == 0)
                throw new InvalidOperationException(
                    "SqlSugarOptions.EnableInitTables 已开启，但 InitTablesEntityTypes 为空 —— " +
                    "那样不会建任何表（静默失效）。请列出需要建表的实体类型，或关闭该选项。");

            services.AddHostedService<SqlSugarInitTablesHostedService>();
        }

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
