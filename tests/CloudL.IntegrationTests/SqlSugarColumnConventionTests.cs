using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CloudL.Domain.Entities;
using CloudL.SqlSugar;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// 列约定：用 **SQLite 的真实 DDL** 验证（离线可跑）。
/// </summary>
/// <remarks>
/// <para><strong>为什么不用 <c>EntityMaintenance.GetEntityInfo&lt;T&gt;()</c></strong>：那条路径
/// <strong>不会应用</strong> <c>ConnectionConfig.ConfigureExternalServices</c>（实测五种库的 DataType
/// 都停留在 SqlSugar 的默认值 <c>TIMESTAMP</c>），因此拿它断言列约定只能得到假结论。
/// 真实建表（<c>CodeFirst.InitTables</c>）才会应用配置 —— 所以这里改成建表后读回 schema。</para>
/// <para><strong>仍未覆盖的（需要在真实数据库上验证）</strong>：PostgreSQL / SqlServer / <strong>达梦</strong> /
/// Oracle / MySql 各自的列类型串（<c>timestamp without time zone</c> / <c>datetime2</c> / <c>TIMESTAMP</c> /
/// <c>VARCHAR2(256)</c> / <c>varchar(256)</c>）无法在离线环境生成 DDL，只能在对应数据库上跑一次：
/// 执行 <c>EnableInitTables</c>（或迁移脚本）后检查 <c>information_schema</c>。达梦侧见
/// <c>DmDatabaseTests</c>（字符串约定是 <c>NVARCHAR2(256)</c>：达梦 <c>VARCHAR(n)</c> 按字节计，
/// 256 只装得下 85 个汉字；<c>NVARCHAR2(n)</c> 按字符计）。</para>
/// </remarks>
public class SqlSugarColumnConventionTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _provider;

    public SqlSugarColumnConventionTests()
    {
        var name = "mem_" + Guid.NewGuid().ToString("N");
        var connectionString = $"DataSource=file:{name}?mode=memory&cache=shared";

        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();

        var services = new ServiceCollection();
        services.AddCloudLSqlSugar(options =>
        {
            options.ConnectionString = connectionString;
            options.DbType = "Sqlite";
            options.EnableInitTables = true;
            options.InitTablesEntityTypes.Add(typeof(ConventionTestItem));
        });

        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _keepAlive.Dispose();
    }

    [Fact]
    public async Task InitTables_ShouldApplyColumnConventions()
    {
        var client = _provider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();

        // 抓真实下发的建表 SQL（达梦那边就是这条 SQL 报「语法分析出错」的）
        var executed = new List<string>();
        client.Aop.OnLogExecuting = (sql, _) => executed.Add(sql);

        // 走真实建表路径（会应用 ConfigureExternalServices）
        client.CodeFirst.InitTables<ConventionTestItem>();

        var columns = await client.Ado.SqlQueryAsync<dynamic>("PRAGMA table_info(ConventionTestItem)");
        var byName = new Dictionary<string, (string Type, long NotNull, long Pk)>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in columns)
        {
            var map = (IDictionary<string, object>)row;
            byName[Convert.ToString(map["name"])!] = (
                Convert.ToString(map["type"]) ?? string.Empty,
                Convert.ToInt64(map["notnull"]),
                Convert.ToInt64(map["pk"]));
        }

        Assert.NotEmpty(byName);

        // 主键必须被声明（否则 Updateable/Deleteable 会抛异常）
        Assert.True(byName["Id"].Pk > 0, "Id 必须是主键");

        // 时间列：非空的那个应存在；可空的必须允许 NULL（显式 DataType 曾把可空性丢掉）
        Assert.True(byName.ContainsKey("Moment"));
        Assert.Equal(0L, byName["OptionalMoment"].NotNull);

        // 引用类型默认可空（与 EF 一致）
        Assert.Equal(0L, byName["Name"].NotNull);

        // 作者**显式**写了 IsNullable = false 时，约定不得把它吞掉（此前对引用类型无条件补 true，
        // 结果任何 string 都建不出 NOT NULL）。SqlSugar 的 SugarColumn.IsNullable 是 bool 默认 false，
        // 所以框架改看元数据（CustomAttributeData.NamedArguments）区分「显式 false」与「没配」。
        Assert.Equal(1L, byName["RequiredName"].NotNull);

        // 反向守卫：只写了 ColumnDataType、**没写** IsNullable 的可空类型，仍必须是 NULL
        // （否则「看特性有没有写 IsNullable」会被误实现成「只要有 SugarColumn 特性就 NOT NULL」）
        Assert.Equal(0L, byName["TypedOptional"].NotNull);

        // 类型串自带括号 ⇒ Length 必须归零（此前只覆盖「框架自己写的类型串」，作者的写法漏网）：
        // [SugarColumn(ColumnDataType = "NVARCHAR2(256)", Length = 200)] 会被 SqlSugar 拼成
        // NVARCHAR2(256)(200) —— 达梦报「语法分析出错」，SQLite 也直接语法错（建表就抛异常）。
        // 作者显式写的类型串，最终列类型必须**恰好**是作者写的那一串，长度不多不少。
        Assert.Equal("NVARCHAR2(256)", byName["TypedWithLength"].Type);
        Assert.DoesNotContain(")(", byName["TypedWithLength"].Type);

        // 断言到生成的建表 SQL 本身（不只是读回的类型列）：整条 DDL 里不得出现 ")("
        var createSql = executed.Single(sql =>
            sql.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("TypedWithLength", StringComparison.Ordinal));

        Assert.DoesNotContain(")(", createSql);

        // [NotPersisted] 成员不得建列
        Assert.False(byName.ContainsKey("DomainEvents"), "DomainEvents 标了 [NotPersisted]，不应成为列");
    }
}

/// <summary>列约定测试实体：覆盖时间、可空时间、字符串与继承来的 [NotPersisted] 成员。</summary>
public sealed class ConventionTestItem : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;

    /// <summary>显式要求 NOT NULL 的字符串（作者写了 <c>IsNullable = false</c>）。</summary>
    [global::SqlSugar.SugarColumn(IsNullable = false)]
    public string RequiredName { get; set; } = string.Empty;

    /// <summary>只写了类型、没写可空性 —— 约定必须按 CLR 类型补成可空。</summary>
    [global::SqlSugar.SugarColumn(ColumnDataType = "TEXT")]
    public string? TypedOptional { get; set; }

    /// <summary>
    /// 作者显式写了<strong>自带括号</strong>的完整类型串**又**写了 <c>Length</c> —— SqlSugar 建表时会把
    /// <c>Length</c> 再拼一次，生成 <c>NVARCHAR2(256)(200)</c>（达梦/SQLite 都报语法错）。
    /// 框架必须把这种 <c>Length</c> 归零（规则对作者写的类型串与框架写的类型串一视同仁）。
    /// </summary>
    [global::SqlSugar.SugarColumn(ColumnDataType = "NVARCHAR2(256)", Length = 200)]
    public string TypedWithLength { get; set; } = string.Empty;

    public DateTime Moment { get; set; }

    public DateTime? OptionalMoment { get; set; }
}
