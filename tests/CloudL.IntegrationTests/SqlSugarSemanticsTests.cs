using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CloudL.Domain.Entities;
using CloudL.SqlSugar;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// SqlSugar 与 EF 的<strong>语义对齐</strong>回归：墙上钟守卫、单行查询语义。
/// </summary>
public class SqlSugarSemanticsTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _provider;
    private readonly SqlSugarRepository<SemanticsTestItem, Guid> _repository;

    public SqlSugarSemanticsTests()
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
        });

        _provider = services.BuildServiceProvider();
        var client = _provider.GetRequiredService<ISqlSugarClient>();
        _repository = new SqlSugarRepository<SemanticsTestItem, Guid>(
            client,
            _provider.GetRequiredService<SqlSugarUnitOfWork>());

        client.CodeFirst.InitTables<SemanticsTestItem>();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _keepAlive.Dispose();
    }

    [Fact]
    public async Task AddAsync_WithUtcKind_ShouldNormalizeToWallClock()
    {
        var item = new SemanticsTestItem
        {
            Name = "utc",
            Moment = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc)
        };

        await _repository.AddAsync(item);

        // 修复前这里仍然是 Utc：写 PostgreSQL 会直接抛异常（表现为 500），写别的库会静默存错口径
        Assert.Equal(DateTimeKind.Unspecified, item.Moment.Kind);
        Assert.Equal(new DateTime(2026, 9, 30, 12, 0, 0), item.Moment);
    }

    [Fact]
    public async Task UpdateAsync_WithUtcKind_ShouldNormalizeToWallClock()
    {
        var item = new SemanticsTestItem { Name = "before", Moment = new DateTime(2026, 9, 30, 1, 0, 0) };
        await _repository.AddAsync(item);

        item.Moment = new DateTime(2026, 9, 30, 3, 0, 0, DateTimeKind.Utc);

        await _repository.UpdateAsync(item, item.RowVersion);

        Assert.Equal(DateTimeKind.Unspecified, item.Moment.Kind);
    }

    [Fact]
    public async Task FindSingleAsync_WithMultipleMatches_ShouldThrow_LikeEfCore()
    {
        await _repository.AddRangeAsync(new[]
        {
            new SemanticsTestItem { Name = "dup" },
            new SemanticsTestItem { Name = "dup" }
        });

        // EF 侧是 SingleOrDefaultAsync：多条匹配抛异常；SqlSugar 侧此前用 FirstAsync 会静默取首条
        await Assert.ThrowsAnyAsync<Exception>(() => _repository.FindSingleAsync(x => x.Name == "dup"));
    }
}

/// <summary>测试实体：含可写的 DateTime 列（用于验证墙上钟守卫）。</summary>
public sealed class SemanticsTestItem : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;

    public DateTime Moment { get; set; }
}
