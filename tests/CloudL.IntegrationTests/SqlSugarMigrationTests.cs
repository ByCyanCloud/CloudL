using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CloudL.SqlSugar;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// 迁移执行器（方案 C）：脚本按版本执行一次、可重复执行、失败回滚且不留历史。
/// </summary>
public class SqlSugarMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cloudl_migrations_" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _provider;
    private readonly ISqlSugarClient _client;

    public SqlSugarMigrationTests()
    {
        Directory.CreateDirectory(_directory);

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
        _client = _provider.GetRequiredService<ISqlSugarClient>();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _keepAlive.Dispose();

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string WriteScript(string version, string sql)
    {
        var path = Path.Combine(_directory, version + ".sql");
        File.WriteAllText(path, sql);

        return path;
    }

    [Fact]
    public async Task MigrateAsync_ShouldApplyOnceAndBeIdempotent()
    {
        WriteScript("0001_create", "CREATE TABLE migrate_demo (Id TEXT NOT NULL PRIMARY KEY, Name TEXT NULL);");
        WriteScript("0002_seed", "INSERT INTO migrate_demo (Id, Name) VALUES ('a', 'first');");

        var runner = new SqlSugarMigrationRunner(_client);

        var first = await runner.MigrateAsync(_directory);

        Assert.Equal(new[] { "0001_create", "0002_seed" }, first);

        // 第二次运行：全部已应用，不应重复执行（否则 INSERT 会插第二条）
        var second = await runner.MigrateAsync(_directory);
        Assert.Empty(second);

        var rows = await _client.Ado.SqlQueryAsync<dynamic>("SELECT COUNT(1) AS C FROM migrate_demo");
        Assert.Equal(1L, Convert.ToInt64(((IDictionary<string, object>)rows[0])["C"]));

        var applied = await runner.GetAppliedVersionsAsync();
        Assert.Equal(new[] { "0001_create", "0002_seed" }, applied);
    }

    [Fact]
    public async Task MigrateAsync_WhenScriptFails_ShouldRollbackAndNotRecordHistory()
    {
        WriteScript("0001_good", "CREATE TABLE ok_table (Id TEXT NOT NULL PRIMARY KEY);");
        WriteScript("0002_bad", "CREATE TABLE broken_table (Id TEXT NOT NULL PRIMARY KEY); THIS IS NOT SQL;");

        var runner = new SqlSugarMigrationRunner(_client);

        await Assert.ThrowsAnyAsync<Exception>(() => runner.MigrateAsync(_directory));

        var applied = await runner.GetAppliedVersionsAsync();

        Assert.Contains("0001_good", applied);
        Assert.DoesNotContain("0002_bad", applied);   // 失败脚本不得留下历史

        // 失败脚本在同一个事务里，其建表必须被回滚
        var tables = await _client.Ado.SqlQueryAsync<dynamic>(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'broken_table'");

        Assert.Empty(tables);
    }
}
