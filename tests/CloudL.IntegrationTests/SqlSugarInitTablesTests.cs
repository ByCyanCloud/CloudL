using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudL.Domain.Entities;
using CloudL.SqlSugar;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// <c>EnableInitTables</c> 的接线回归。
/// </summary>
/// <remarks>
/// 此前它只是<strong>声明了却没人读</strong>的选项：打开后什么都不会发生，而文档还教用户去设置它 ——
/// 典型静默失效。现在它真的会在宿主启动时建表，且<strong>开启却没给实体清单会直接抛异常</strong>。
/// </remarks>
public class SqlSugarInitTablesTests
{
    private static ServiceCollection BuildServices(string connectionString, Action<SqlSugarOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddCloudLSqlSugar(options =>
        {
            options.ConnectionString = connectionString;
            options.DbType = "Sqlite";   // 不设就是默认的 Dm（达梦），测试会去连达梦而失败
            configure(options);
        });

        return services;
    }

    [Fact]
    public void EnableInitTables_WithoutEntityTypes_ShouldFailFast()
    {
        var name = "mem_" + Guid.NewGuid().ToString("N");
        var connectionString = $"DataSource=file:{name}?mode=memory&cache=shared";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BuildServices(connectionString, options => options.EnableInitTables = true));

        Assert.Contains("静默失效", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnableInitTables_WithEntityTypes_ShouldCreateTablesOnStart()
    {
        var name = "mem_" + Guid.NewGuid().ToString("N");
        var connectionString = $"DataSource=file:{name}?mode=memory&cache=shared";

        using var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        var services = BuildServices(connectionString, options =>
        {
            options.EnableInitTables = true;
            options.InitTablesEntityTypes.Add(typeof(InitTablesTestItem));
        });

        await using var provider = services.BuildServiceProvider();

        // 模拟宿主启动
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        var client = provider.GetRequiredService<global::SqlSugar.ISqlSugarClient>();
        var tables = await client.Ado.SqlQueryAsync<dynamic>(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'InitTablesTestItem'");

        Assert.NotEmpty(tables);
    }
}

/// <summary>InitTables 测试实体。</summary>
public sealed class InitTablesTestItem : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;
}
