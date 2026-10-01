using Microsoft.Extensions.Logging;
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
        services.AddSingleton<ISqlSugarClient>(serviceProvider => new SqlSugarScope(
            new ConnectionConfig
            {
                ConnectionString = options.ConnectionString,
                DbType = dbType,
                IsAutoCloseConnection = true,
                // 标识符大小写：SqlSugar 默认把标识符转成大写下发。达梦实例 CASE_SENSITIVE=1 且库表
                // 是小写（organizations / created_at）时，转大写会让 ORM 报「无效的表或视图名」——
                // 表在库里、原生 SQL 读得到，只有 ORM 读不了。由业务侧显式设置 IsAutoToUpper=false。
                MoreSettings = new ConnMoreSettings { IsAutoToUpper = options.IsAutoToUpper },
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
                        var sugarColumn = property.GetCustomAttribute<SugarColumn>();
                        var explicitType = sugarColumn?.ColumnDataType;

                        if (string.IsNullOrWhiteSpace(explicitType))
                        {
                            // 只有**我们自己**写过完整类型串时才需要把 Length 归零（见下面那段注释）：
                            // 别的 CLR 类型保持 SqlSugar 的原样处理，不去动它。
                            var overrodeDataType = false;

                            if (property.PropertyType == typeof(string))
                            {
                                // 长度：作者显式写了 Length 就以它为准（迁移历史表的 Version 需要 512 ——
                                // 脚本文件名即版本号，长文件名不能被静默截断成默认的 256）；否则用框架默认常量。
                                // 只认作者真正写下的 Length（判据同 HasExplicitNullability，看元数据），
                                // 免得把 SqlSugar 的全局默认长度误当成作者意图。
                                var stringLength = GetExplicitLength(property) ?? AppConstants.DefaultStringMaxLength;

                                column.DataType = dbType switch
                                {
                                    global::SqlSugar.DbType.PostgreSQL => $"character varying({stringLength})",
                                    global::SqlSugar.DbType.SqlServer => $"nvarchar({stringLength})",
                                    // 达梦的 VARCHAR(n) 按**字节**计（UTF-8 下一个汉字 3 字节）：256 只装得下
                                    // 85 个汉字；NVARCHAR2(n) 按**字符**计（实测 50 个汉字正好、51 个报超长），
                                    // 与 EF 侧 IsUnicode(true) 的意图一致。项目现有列就是 NVARCHAR2(n)。
                                    global::SqlSugar.DbType.Dm => $"NVARCHAR2({stringLength})",
                                    global::SqlSugar.DbType.Oracle => $"VARCHAR2({stringLength})",
                                    global::SqlSugar.DbType.MySql => $"varchar({stringLength})",
                                    _ => "TEXT"
                                };

                                overrodeDataType = true;
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

                                overrodeDataType = true;
                            }

                            if (overrodeDataType)
                            {
                                // 上面赋的是**完整类型串**（自带括号）。SqlSugar 建表时会把 column.Length
                                // 再拼一次 → 得到 "NVARCHAR2(256)(200)" 这种语法垃圾，达梦直接报
                                // 「第 N 行附近出现错误: 语法分析出错」：迁移执行器连自己的历史表都建不出来
                                // （SchemaHistoryRow.Version 的 Length 与约定叠加），方案 C 完全不可用。
                                // 归零即可 —— 长度已经写在类型串里了。
                                // 迁移历史表实体走的正是这条路径（Version 未显式写 ColumnDataType），
                                // 与下面作者显式写 ColumnDataType 的那条分支**同一条规则**。
                                column.Length = 0;
                            }
                        }
                        else if (TypeStringCarriesItsOwnLength(explicitType))
                        {
                            // 同一条规则的作者版本：作者显式写下的完整类型串只要自带括号（如
                            // [SugarColumn(ColumnDataType = "NVARCHAR2(256)", Length = 200)]），
                            // 也必须把 Length 归零 —— 否则 SqlSugar 会拼成 NVARCHAR2(256)(200)，
                            // 达梦报「语法分析出错」。此前这条只覆盖了框架自己写的类型串，作者的写法漏网。
                            column.Length = 0;
                        }

                        // 可空性：显式设置 DataType 会丢掉它（曾把可空列建成 NOT NULL）；按 CLR 类型补回，与 EF 默认一致。
                        // 但**作者显式写下的 IsNullable 不能被吞掉**：SqlSugar 的 SugarColumn.IsNullable 是 bool
                        // （默认 false），拿到特性实例后区分不出「显式写了 false」与「压根没配」——
                        // 因此判据看元数据（CustomAttributeData.NamedArguments 只含作者真正写下的实参）。
                        if (!HasExplicitNullability(property)
                            && (property.PropertyType.IsClass || Nullable.GetUnderlyingType(property.PropertyType) is not null))
                        {
                            column.IsNullable = true;
                        }

                        // 框架基类成员的列名必须与 CONTRACT.md §6 冻结的 6 个列名一致
                        // （id / created_at / row_version / updated_at / created_by / updated_by）。
                        // 这 6 个属性声明在 CloudL.Core 的基类上，业务实体**无法**给它们挂
                        // [SugarColumn(ColumnName=...)]；EF 侧靠 BaseEntityConfiguration 的 HasColumnName
                        // 声明，SqlSugar 看不到那份配置 —— 于是列名会退化成 CREATEDAT / ROWVERSION /
                        // UPDATEDAT / CREATEDBY / UPDATEDBY，与现有库对不上，ORM 直接读不了表。
                        // 只在业务实体**没有显式声明 ColumnName** 时补默认值（显式声明优先）。
                        if (string.IsNullOrWhiteSpace(sugarColumn?.ColumnName)
                            && GetBaseClassColumnName(property) is { } baseColumnName)
                        {
                            column.DbColumnName = baseColumnName;
                        }

                        // 主键：框架把主键定义在 Entity<TKey>.Id 上（EF 侧由 BaseEntityConfiguration 声明），
                        // SqlSugar 看不到那份配置 —— 不标出来，Updateable(entity)/Deleteable(entity) 会直接抛异常
                        if (property.Name == "Id"
                            && property.DeclaringType is { IsGenericType: true } declaring
                            && declaring.GetGenericTypeDefinition() == typeof(Entity<>))
                        {
                            column.IsPrimarykey = true;

                            // Guid 主键的列类型必须跟**EF 侧建出来的库**一致，否则 InitTables 会反复 ALTER。
                            // SqlSugar 默认给 varchar(36)，与 EF 各 provider 的实际映射都不同：
                            //   达梦 CHAR(36)（实测现有库如此）｜PostgreSQL uuid｜SqlServer uniqueidentifier｜
                            //   MySql char(36)｜Oracle 保持 SqlSugar 默认（未验证，不猜）。
                            if (property.PropertyType == typeof(Guid))
                            {
                                var guidDataType = dbType switch
                                {
                                    global::SqlSugar.DbType.Dm => "CHAR(36)",
                                    global::SqlSugar.DbType.PostgreSQL => "uuid",
                                    global::SqlSugar.DbType.SqlServer => "uniqueidentifier",
                                    global::SqlSugar.DbType.MySql => "char(36)",
                                    _ => null
                                };

                                if (guidDataType is not null)
                                {
                                    column.DataType = guidDataType;
                                    column.Length = 0;   // 类型串自带括号，别让 SqlSugar 再拼一次
                                }
                            }
                        }

                        // 忽略规则来自 CloudL.Core 的中立声明（NotPersistedAttribute），不依赖任何具体 ORM 的配置
                        if (property.IsDefined(typeof(NotPersistedAttribute), inherit: true))
                        {
                            column.IsIgnore = true;
                        }

                        // 主键不可能为 NULL —— 达梦/Oracle 的数据字典对主键列一律报 NOT NULL（实测
                        // ALL_TAB_COLUMNS.NULLABLE = 'N'），而模型侧的 IsNullable 若为 true
                        // （string 主键会走上面「引用类型默认可空」那条规则），SqlSugar 每次 InitTables
                        // 都会生成 ALTER TABLE ... modify (... null ...) 想把列改回可空，
                        // 达梦直接报「无效的表[...]约束」→ **第二次 InitTables 必崩**：
                        // 迁移执行器第二遍就起不来（第一遍建表成功、第二遍改结构失败）。
                        // 按主键语义强制 NOT NULL，让模型与数据字典一致。
                        if (column.IsPrimarykey)
                        {
                            column.IsNullable = false;
                        }
                    }
                }            },
            client =>
            {
                if (options.EnableSqlLog)
                {
                    var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("CloudL.SqlSugar");

                    client.Aop.OnLogExecuting = (sql, _) =>
                        logger?.LogDebug("SqlSugar 执行 SQL：{Sql}", sql);
                }
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
    /// 类型串是否<strong>自带长度</strong>（含括号，如 <c>NVARCHAR2(256)</c> / <c>CHAR(36)</c> /
    /// <c>character varying(256)</c>）。
    /// </summary>
    /// <remarks>
    /// <para>这是「类型串自带括号 ⇒ 把 <c>EntityColumnInfo.Length</c> 归零」这条规则的判据：
    /// SqlSugar 建表时会把 <c>Length</c> 再拼一次，类型串里已经写了长度却不清零，
    /// 就会生成 <c>NVARCHAR2(256)(200)</c> 这种语法垃圾（达梦报「语法分析出错」）。</para>
    /// <para><strong>两种来源一视同仁</strong>：作者在 <c>[SugarColumn(ColumnDataType = ...)]</c> 里显式写下的
    /// 类型串直接由本判据判定（此前漏网）；框架自己按 provider 写下的类型串则一律归零 ——
    /// 框架写的字符串类型串全部自带括号（达梦 <c>NVARCHAR2(n)</c> 等），时间类型串不带括号、<c>Length</c>
    /// 本来也不该带（保持框架原有行为不变）。迁移历史表实体走的正是框架这条路径。</para>
    /// </remarks>
    private static bool TypeStringCarriesItsOwnLength(string? dataType) =>
        !string.IsNullOrWhiteSpace(dataType) && dataType.Contains('(');

    /// <summary>
    /// 作者显式写的 <c>SugarColumn.Length</c>（没写 / 写成非正数时返回 <c>null</c>）。
    /// </summary>
    /// <remarks>
    /// 判据与 <see cref="HasExplicitNullability"/> 同源：看<strong>元数据</strong>
    /// （<see cref="CustomAttributeData.NamedArguments"/> 只含作者真正写下的实参），
    /// 而不是看 <c>EntityColumnInfo.Length</c> —— 后者会被 SqlSugar 的全局默认长度（或 0）干扰。
    /// </remarks>
    private static int? GetExplicitLength(PropertyInfo property)
    {
        foreach (var data in property.GetCustomAttributesData())
        {
            if (data.AttributeType != typeof(SugarColumn))
                continue;

            foreach (var argument in data.NamedArguments)
            {
                if (argument.MemberName == nameof(SugarColumn.Length)
                    && argument.TypedValue.Value is int length
                    && length > 0)
                {
                    return length;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 作者是否<strong>显式</strong>写过 <c>SugarColumn.IsNullable</c>。
    /// </summary>
    /// <remarks>
    /// <para><c>SugarColumn.IsNullable</c> 是 <c>bool</c>、默认 <c>false</c>，所以拿到特性<strong>实例</strong>后
    /// 区分不出「作者显式写了 <c>false</c>」与「作者压根没配」—— 而这两种情况的期望完全相反：
    /// 前者要建 <c>NOT NULL</c>（如项目的 <c>code</c> / <c>name</c>），后者要按 CLR 类型补成可空
    /// （与 EF 默认一致，否则 <c>[SugarColumn(ColumnDataType="NVARCHAR2(50)")]</c> 这种只写类型的地方会被误建成 NOT NULL）。</para>
    /// <para>因此判据必须看<strong>元数据</strong>：<see cref="CustomAttributeData.NamedArguments"/> 只包含
    /// 作者真正写下的命名实参（<c>[SugarColumn(IsNullable = false)]</c> 有，<c>[SugarColumn(ColumnDataType = "...")]</c> 没有）。</para>
    /// </remarks>
    private static bool HasExplicitNullability(PropertyInfo property) =>
        property.GetCustomAttributesData().Any(data =>
            data.AttributeType == typeof(SugarColumn)
            && data.NamedArguments.Any(argument => argument.MemberName == nameof(SugarColumn.IsNullable)));

    /// <summary>
    /// 框架基类成员对应的数据库列名（<c>id</c> / <c>created_at</c> / <c>row_version</c> /
    /// <c>updated_at</c> / <c>created_by</c> / <c>updated_by</c>，见 CONTRACT.md §6）。
    /// </summary>
    /// <remarks>
    /// 这 6 个属性声明在 <c>CloudL.Core</c> 的基类上（<c>Entity</c> / <c>Entity&lt;TKey&gt;</c> /
    /// <c>AuditableEntity</c> / <c>AuditableEntity&lt;TKey&gt;</c>），业务实体<strong>无法</strong>给它们挂
    /// <c>[SugarColumn(ColumnName = ...)]</c>（那会往 Core 引 SqlSugar 依赖）。EF 侧由
    /// <c>BaseEntityConfiguration</c> 的 <c>HasColumnName</c> 声明，SqlSugar 看不到那份配置，
    /// 于是列名退化成 <c>CREATEDAT</c> / <c>ROWVERSION</c> / <c>UPDATEDAT</c> / <c>CREATEDBY</c> / <c>UPDATEDBY</c>，
    /// 与现有库的 <c>created_at</c> / <c>row_version</c> / … 对不上 —— 表现为 ORM 读不了这些列。
    /// </remarks>
    private static string? GetBaseClassColumnName(PropertyInfo property)
    {
        var declaringType = property.DeclaringType;
        if (declaringType is null)
            return null;

        var definition = declaringType.IsGenericType
            ? declaringType.GetGenericTypeDefinition()
            : declaringType;

        if (definition == typeof(Entity))
        {
            return property.Name switch
            {
                nameof(Entity.CreatedAt) => "created_at",
                nameof(Entity.RowVersion) => "row_version",
                _ => null
            };
        }

        if (definition == typeof(Entity<>))
        {
            return property.Name == nameof(Entity<Guid>.Id) ? "id" : null;
        }

        if (definition == typeof(AuditableEntity) || definition == typeof(AuditableEntity<>))
        {
            return property.Name switch
            {
                nameof(IAuditable.UpdatedAt) => "updated_at",
                nameof(IAuditable.CreatedBy) => "created_by",
                nameof(IAuditable.UpdatedBy) => "updated_by",
                _ => null
            };
        }

        return null;
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
