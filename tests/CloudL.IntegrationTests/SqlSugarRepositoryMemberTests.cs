using System;
using System.Threading.Tasks;
using CloudL.Domain.Entities;
using CloudL.Domain.Repositories;
using CloudL.SqlSugar;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// SqlSugar 仓储的成员覆盖：把此前没有任何测试的公开方法逐个钉住
/// （审计指出这些成员"改坏无提示"）。
/// </summary>
public class SqlSugarRepositoryMemberTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _provider;
    private readonly SqlSugarRepository<MemberTestItem, Guid> _repository;

    public SqlSugarRepositoryMemberTests()
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
        var client = _provider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        _repository = new SqlSugarRepository<MemberTestItem, Guid>(
            client,
            _provider.GetRequiredService<SqlSugarUnitOfWork>());

        client.CodeFirst.InitTables<MemberTestItem>();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _keepAlive.Dispose();
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnEntity()
    {
        var item = await _repository.AddAsync(new MemberTestItem { Name = "byid" });

        var loaded = await _repository.GetByIdAsync(item.Id);

        Assert.NotNull(loaded);
        Assert.Equal("byid", loaded!.Name);
    }

    [Fact]
    public async Task AddRangeAsync_ShouldInsertAll()
    {
        await _repository.AddRangeAsync(new[]
        {
            new MemberTestItem { Name = "r1" },
            new MemberTestItem { Name = "r2" },
            new MemberTestItem { Name = "r3" }
        });

        Assert.Equal(3, await _repository.CountAsync());
    }

    [Fact]
    public async Task FindAsync_ShouldReturnMatches()
    {
        await _repository.AddRangeAsync(new[]
        {
            new MemberTestItem { Name = "hit" },
            new MemberTestItem { Name = "hit" },
            new MemberTestItem { Name = "miss" }
        });

        var found = await _repository.FindAsync(x => x.Name == "hit");

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task FindSingleForUpdateAsync_ShouldReturnEntity()
    {
        await _repository.AddAsync(new MemberTestItem { Name = "forupdate" });

        var loaded = await _repository.FindSingleForUpdateAsync(x => x.Name == "forupdate");

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task ExistsAsync_ShouldReflectPresence()
    {
        await _repository.AddAsync(new MemberTestItem { Name = "exists" });

        Assert.True(await _repository.ExistsAsync(x => x.Name == "exists"));
        Assert.False(await _repository.ExistsAsync(x => x.Name == "nope"));
    }

    [Fact]
    public async Task CountAsync_WithPredicate_ShouldFilter()
    {
        await _repository.AddRangeAsync(new[]
        {
            new MemberTestItem { Name = "counted" },
            new MemberTestItem { Name = "counted" },
            new MemberTestItem { Name = "other" }
        });

        Assert.Equal(2, await _repository.CountAsync(x => x.Name == "counted"));
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveEntity()
    {
        var item = await _repository.AddAsync(new MemberTestItem { Name = "del" });

        await _repository.DeleteAsync(item);

        Assert.Equal(0, await _repository.CountAsync());
    }

    [Fact]
    public async Task DeleteByIdAsync_ShouldRemoveEntity()
    {
        var item = await _repository.AddAsync(new MemberTestItem { Name = "delbyid" });

        await _repository.DeleteByIdAsync(item.Id);

        Assert.Equal(0, await _repository.CountAsync());
    }

    [Fact]
    public async Task AddAsync_WithNonGuidKeyNotGenerated_ShouldThrowClearError()
    {
        var client = _provider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        var repository = new SqlSugarRepository<NonGuidKeyItem, int>(client);

        // SqlSugar 不会自动生成主键；非 Guid 主键必须由业务在建实体时给，否则要抛清晰错误（而不是静默覆盖）
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.AddAsync(new NonGuidKeyItem()));

        Assert.Contains("不会自动生成主键", exception.Message, StringComparison.Ordinal);
    }
}

/// <summary>成员覆盖测试实体。</summary>
public sealed class MemberTestItem : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>非 Guid 主键实体：用于验证"未生成主键"的清晰报错分支。</summary>
public sealed class NonGuidKeyItem : Entity<int>
{
    public string Name { get; set; } = string.Empty;
}
