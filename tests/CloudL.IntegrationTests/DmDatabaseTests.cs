using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CloudL.Domain.Entities;
using CloudL.SqlSugar;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// 达梦（DM）真库验证：列约定、CRUD、事务回滚。
/// </summary>
/// <remarks>
/// <para>连接串来自环境变量 <see cref="DmFactAttribute.ConnectionVariable"/>（<strong>不写进仓库</strong>）。
/// 未设置时这些用例显示为 <strong>Skipped</strong>（而不是静默通过 —— 静默通过正是一整天在防的那类假绿）。</para>
/// <para>这是唯一能验证"达梦上列类型/CRUD 是否正确"的方式：离线只能验 SQLite。</para>
/// </remarks>
public class DmDatabaseTests
{
    private static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DmFactAttribute.ConnectionVariable));

    private static ServiceProvider BuildProvider()
    {
        var connectionString = Environment.GetEnvironmentVariable(DmFactAttribute.ConnectionVariable);

        var services = new ServiceCollection();
        services.AddCloudLSqlSugar(options =>
        {
            options.ConnectionString = connectionString!;
            options.DbType = "Dm";
        });

        return services.BuildServiceProvider();
    }

    [DmFact]
    public async Task Dm_ShouldApplyColumnConventions()
    {
        if (!IsConfigured)
            return;

        await using var provider = BuildProvider();

        var client = provider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();

        client.CodeFirst.InitTables<DmConventionItem>();

        // 达梦兼容 Oracle 数据字典；标识符默认大写
        var rows = await client.Ado.SqlQueryAsync<dynamic>(
            "SELECT COLUMN_NAME, DATA_TYPE, DATA_LENGTH, CHAR_LENGTH, NULLABLE FROM ALL_TAB_COLUMNS WHERE TABLE_NAME = 'DMCONVENTIONITEM'");

        var columns = new Dictionary<string, (string Type, long Length, long CharLength, string Nullable)>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var map = (IDictionary<string, object>)row;

            columns[Convert.ToString(map["COLUMN_NAME"])!] = (
                Convert.ToString(map["DATA_TYPE"]) ?? string.Empty,
                Convert.ToInt64(map["DATA_LENGTH"]),
                Convert.ToInt64(map["CHAR_LENGTH"]),
                Convert.ToString(map["NULLABLE"]) ?? string.Empty);
        }

        Assert.NotEmpty(columns);

        // 时间列必须不带时区
        Assert.Contains("TIMESTAMP", columns["MOMENT"].Type, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TIME ZONE", columns["MOMENT"].Type, StringComparison.OrdinalIgnoreCase);

        // 字符串默认长度与 EF 侧同一个常量（256）。
        // 达梦的 VARCHAR(n) 按**字节**计（UTF-8 下一个汉字 3 字节），NVARCHAR2(n) 按**字符**计 ——
        // 框架给达梦的约定**必须是 NVARCHAR2(256)**，否则同一份 256 长度中文只能装 85 字，
        // 与现有库里 NVARCHAR2(n) 的列（中文按字计）对不上。所以断言字符数而不是字节数：
        // 实测 NVARCHAR2(256) 的 DATA_LENGTH = 1024（字节），CHAR_LENGTH = 256（字符）。
        Assert.Equal("NVARCHAR2", columns["NAME"].Type, ignoreCase: true);
        Assert.Equal(256L, columns["NAME"].CharLength);

        // 可空列必须真的可空
        Assert.Equal("Y", columns["OPTIONALMOMENT"].Nullable.ToUpperInvariant());
        Assert.Equal("Y", columns["NAME"].Nullable.ToUpperInvariant());

        // 框架基类字段的列名是 CONTRACT.md §6 冻结的 snake_case（不是 CREATEDAT / ROWVERSION），
        // 否则 ORM 读不了现有库里 created_at / row_version 这些列
        Assert.True(columns.ContainsKey("CREATED_AT"), "框架基类字段 CreatedAt 必须映射为 created_at");
        Assert.True(columns.ContainsKey("ROW_VERSION"), "框架基类字段 RowVersion 必须映射为 row_version");

        // Guid 主键必须是 CHAR(36)（与现有库一致，而不是 SqlSugar 默认的 varchar(36)）
        Assert.Equal("CHAR", columns["ID"].Type, ignoreCase: true);

        // [NotPersisted] 成员不得建列
        Assert.False(columns.ContainsKey("DOMAINEVENTS"));
    }

    [DmFact]
    public async Task Dm_ShouldSupportCrudAndTransactionRollback()
    {
        if (!IsConfigured)
            return;

        await using var provider = BuildProvider();

        var client = provider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        var repository = new SqlSugarRepository<DmCrudItem, Guid>(
            client,
            provider.GetRequiredService<SqlSugarUnitOfWork>());

        client.CodeFirst.InitTables<DmCrudItem>();

        // 主键补齐 + 插入 + 读回
        var item = await repository.AddAsync(new DmCrudItem { Name = "dm-crud" });
        Assert.NotEqual(Guid.Empty, item.Id);

        var loaded = await repository.GetByIdAsync(item.Id);
        Assert.NotNull(loaded);
        Assert.Equal("dm-crud", loaded!.Name);

        // 乐观锁：用正确令牌更新
        loaded.Name = "dm-crud-updated";
        await repository.UpdateAsync(loaded, loaded.RowVersion);

        // 事务回滚：抛出后数据不应存在
        var unitOfWork = provider.GetRequiredService<SqlSugarUnitOfWork>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWork.ExecuteInTransactionAsync(async _ =>
            {
                await repository.AddAsync(new DmCrudItem { Name = "should-rollback" });

                throw new InvalidOperationException("模拟业务失败");
            }));

        Assert.False(await repository.ExistsAsync(x => x.Name == "should-rollback"));

        // 清理
        await repository.DeleteByIdAsync(item.Id);
    }

    /// <summary>
    /// 重复 <c>InitTables</c> 不得试图把主键改成可空（达梦把主键列一律报 NOT NULL，模型若认为可空，
    /// SqlSugar 会生成 <c>ALTER TABLE ... modify (... null ...)</c>，达梦报「无效的表[...]约束」——
    /// 表现为 <strong>第二次 InitTables / 第二次跑迁移执行器必崩</strong>）。
    /// </summary>
    /// <remarks>
    /// 必须先建表一次、再跑一次才会触发（第一次是 CREATE，看不出问题），所以这里显式跑两遍。
    /// 用 string 主键：<c>Guid</c> 主键本来就是非空类型，盖不住这条规则。
    /// </remarks>
    [DmFact]
    public async Task Dm_SecondInitTables_WithStringPrimaryKey_ShouldNotFail()
    {
        if (!IsConfigured)
            return;

        await using var provider = BuildProvider();

        var client = provider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();

        try
        {
            client.Ado.ExecuteCommand("DROP TABLE IF EXISTS \"T_CLOUDL_DM_STRINGKEY\"");

            client.CodeFirst.InitTables<DmStringKeyItem>();
            client.CodeFirst.InitTables<DmStringKeyItem>();

            var rows = await client.Ado.SqlQueryAsync<dynamic>(
                "SELECT COLUMN_NAME, NULLABLE FROM ALL_TAB_COLUMNS WHERE TABLE_NAME = 'T_CLOUDL_DM_STRINGKEY'");

            var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var map = (IDictionary<string, object>)row;

                columns[Convert.ToString(map["COLUMN_NAME"])!] = Convert.ToString(map["NULLABLE"]) ?? string.Empty;
            }

            Assert.NotEmpty(columns);

            // 主键列在数据字典里必须是 NOT NULL（与模型一致，第二次 InitTables 才不会去 ALTER 它）
            Assert.Equal("N", columns["ID"].ToUpperInvariant());
        }
        finally
        {
            client.Ado.ExecuteCommand("DROP TABLE IF EXISTS \"T_CLOUDL_DM_STRINGKEY\"");
        }
    }
}

/// <summary>
/// 达梦测试专用的 Fact：未配置 <see cref="ConnectionVariable"/> 时显示为 <strong>Skipped</strong>。
/// </summary>
public sealed class DmFactAttribute : FactAttribute
{
    /// <summary>达梦连接串所在的环境变量名。</summary>
    public const string ConnectionVariable = "CLOUDL_DM_CONNECTION";

    public DmFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
        {
            Skip = "需要真实达梦实例：请设置环境变量 " + ConnectionVariable +
                   "（Server=...;Port=5236;User Id=...;Password=...;）";
        }
    }
}

/// <summary>达梦列约定测试实体。</summary>
public sealed class DmConventionItem : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;

    public DateTime Moment { get; set; }

    public DateTime? OptionalMoment { get; set; }
}

/// <summary>达梦 CRUD 测试实体。</summary>
public sealed class DmCrudItem : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>达梦 string 主键测试实体（历史表 / 复合主键业务实体的形状）。</summary>
[global::SqlSugar.SugarTable("T_CLOUDL_DM_STRINGKEY")]
public sealed class DmStringKeyItem : Entity<string>
{
    public string Name { get; set; } = string.Empty;
}
