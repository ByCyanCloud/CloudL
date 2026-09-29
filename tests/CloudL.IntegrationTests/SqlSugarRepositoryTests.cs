using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CloudL.Domain.Entities;
using CloudL.Domain.Repositories;
using CloudL.Domain.Shared.Persistence;
using CloudL.EntityFrameworkCore;
using CloudL.SqlSugar;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// SqlSugar 第一批：仓储分页语义、事务回滚、以及"两套 ORM 不允许并存"的守卫。
/// </summary>
/// <remarks>
/// <para>用 SQLite 内存库跑（无需达梦实例）。注意内存库是<strong>按连接</strong>隔离的，
/// 所以连接串用 <c>cache=shared</c> 并保持一个"看门"连接不关。</para>
/// <para><strong>关键</strong>：客户端必须通过 <c>AddCloudLSqlSugar</c>（DI）取得，而不是手工 <c>new SqlSugarScope</c> ——
/// 手工创建会绕过包里的映射配置（例如 <c>NotPersistedAttribute</c> 忽略规则），
/// 结果是"测试通过、生产报错"或反过来。走 DI 才测的是生产那一份配置。</para>
/// </remarks>
public class SqlSugarRepositoryTests
{
    private static (ISqlSugarClient Client, SqliteConnection KeepAlive, ServiceProvider Provider) CreateClient()
    {
        var name = "mem_" + Guid.NewGuid().ToString("N");
        var connectionString = $"DataSource=file:{name}?mode=memory&cache=shared";

        var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        var services = new ServiceCollection();
        services.AddCloudLSqlSugar(options =>
        {
            options.ConnectionString = connectionString;
            options.DbType = "Sqlite";
        });

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ISqlSugarClient>();

        client.CodeFirst.InitTables<SqlSugarTestItem>();

        return (client, keepAlive, provider);
    }

    [Fact]
    public async Task Paging_WithTiedSortValues_ShouldNotRepeatOrSkipRows()
    {
        var (client, keepAlive, provider) = CreateClient();
        await using var _ = keepAlive;
        await using var __ = provider;

        var repository = new SqlSugarRepository<SqlSugarTestItem, Guid>(client);

        // 不设置 CreatedAt（它由框架填充，且 setter 不可访问）—— 因此这批数据在排序键上完全并列，
        // 正是"缺少主键次级排序就会翻页重复/漏行"的触发条件。
        for (var index = 0; index < 25; index++)
        {
            await repository.AddAsync(new SqlSugarTestItem { Name = $"item-{index:00}" });
        }

        var collected = new List<Guid>();

        for (var page = 1; page <= 3; page++)
        {
            var result = await repository.GetPagedAsync(page, 10);

            Assert.Equal(25, result.TotalCount);
            Assert.Equal(page, result.PageIndex);
            collected.AddRange(result.Items.Select(item => item.Id));
        }

        Assert.Equal(25, collected.Count);
        Assert.Equal(25, collected.Distinct().Count());
    }

    [Fact]
    public async Task GetPagedAsync_ShouldOrderByGivenFieldAndDirection()
    {
        var (client, keepAlive, provider) = CreateClient();
        await using var _ = keepAlive;
        await using var __ = provider;

        var repository = new SqlSugarRepository<SqlSugarTestItem, Guid>(client);

        for (var index = 0; index < 5; index++)
        {
            await repository.AddAsync(new SqlSugarTestItem { Name = $"n{index}" });
        }

        var ascending = await repository.GetPagedAsync(1, 10, orderBy: item => item.Name, descending: false);
        var descending = await repository.GetPagedAsync(1, 10, orderBy: item => item.Name, descending: true);

        Assert.Equal("n0", ascending.Items[0].Name);
        Assert.Equal("n4", descending.Items[0].Name);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_WhenThrows_ShouldRollback()
    {
        var (client, keepAlive, provider) = CreateClient();
        await using var _ = keepAlive;
        await using var __ = provider;

        var repository = new SqlSugarRepository<SqlSugarTestItem, Guid>(client);
        var unitOfWork = new SqlSugarUnitOfWork(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWork.ExecuteInTransactionAsync(async _ =>
            {
                await repository.AddAsync(new SqlSugarTestItem { Name = "should-be-rolled-back" });

                throw new InvalidOperationException("模拟业务失败");
            }));

        Assert.Equal(0, await repository.CountAsync());
    }

    [Fact]
    public void AddCloudLSqlSugar_WhenEfAlreadyRegistered_ShouldThrow()
    {
        var services = new ServiceCollection();

        services.AddSingleton(new PersistenceProvider(PersistenceProviderKind.EntityFrameworkCore));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCloudLSqlSugar(options =>
            {
                options.ConnectionString = "DataSource=:memory:";
                options.DbType = "Sqlite";
            }));

        Assert.Contains("只允许一套 ORM", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddCloudLEntityFrameworkCore_WhenSqlSugarAlreadyRegistered_ShouldThrow()
    {
        var services = new ServiceCollection();

        services.AddCloudLSqlSugar(options =>
        {
            options.ConnectionString = "DataSource=file:guard?mode=memory&cache=shared";
            options.DbType = "Sqlite";
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCloudLEntityFrameworkCore<WallClockGuardDbContext>(options =>
                options.UseSqlite("DataSource=:memory:")));

        Assert.Contains("只允许一套 ORM", exception.Message, StringComparison.Ordinal);
    }
}

/// <summary>测试实体（框架实体基类，含审计字段 CreatedAt）。</summary>
public sealed class SqlSugarTestItem : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;
}
