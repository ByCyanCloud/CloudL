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
/// 执行 <c>EnableInitTables</c>（或迁移脚本）后检查 <c>information_schema</c>。达梦尤其要在联调时确认。</para>
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

        // [NotPersisted] 成员不得建列
        Assert.False(byName.ContainsKey("DomainEvents"), "DomainEvents 标了 [NotPersisted]，不应成为列");
    }
}

/// <summary>列约定测试实体：覆盖时间、可空时间、字符串与继承来的 [NotPersisted] 成员。</summary>
public sealed class ConventionTestItem : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;

    public DateTime Moment { get; set; }

    public DateTime? OptionalMoment { get; set; }
}
