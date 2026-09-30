using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CloudL.Application.Contracts.IServices;
using CloudL.Domain.Entities;
using CloudL.Domain.Repositories;
using CloudL.SqlSugar;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// SqlSugar 的审计字段：插入填 <c>CreatedBy</c>、更新填 <c>UpdatedBy</c>（与 EF 侧 ApplyAuditFields 同源）。
/// </summary>
/// <remarks>
/// 审计此前在 SqlSugar 侧<strong>完全没实现</strong>：全包 grep 不到 <c>ICurrentUser</c>/<c>CreatedBy</c>，
/// 于是 <c>created_by</c>/<c>updated_by</c> 恒为 NULL 且毫无报错。
/// </remarks>
public class SqlSugarAuditTests : IDisposable
{
    private static readonly Guid CurrentUserId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _provider;

    public SqlSugarAuditTests()
    {
        var name = "mem_" + Guid.NewGuid().ToString("N");
        var connectionString = $"DataSource=file:{name}?mode=memory&cache=shared";

        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new StubCurrentUser(CurrentUserId));
        services.AddCloudLSqlSugar(options =>
        {
            options.ConnectionString = connectionString;
            options.DbType = "Sqlite";
        });

        _provider = services.BuildServiceProvider();

        _provider.GetRequiredService<ISqlSugarClient>().CodeFirst.InitTables<AuditTestItem>();
    }

    private ISqlSugarRepository<AuditTestItem, Guid> Repository
    {
        get
        {
            var scope = _provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ISqlSugarRepository<AuditTestItem, Guid>>();
        }
    }

    public void Dispose()
    {
        _provider.Dispose();
        _keepAlive.Dispose();
    }

    [Fact]
    public async Task AddAsync_ShouldFillCreatedBy()
    {
        var item = new AuditTestItem { Name = "a" };

        await Repository.AddAsync(item);

        Assert.Equal(CurrentUserId, item.CreatedBy);
        Assert.Null(item.UpdatedBy);
    }

    [Fact]
    public async Task UpdateAsync_ShouldFillUpdatedByAndAdvanceToken()
    {
        var item = new AuditTestItem { Name = "b" };
        await Repository.AddAsync(item);

        var token = item.RowVersion;
        item.Name = "b-updated";

        await Repository.UpdateAsync(item, token);

        Assert.Equal(CurrentUserId, item.UpdatedBy);
        Assert.NotNull(item.UpdatedAt);
        Assert.NotEqual(token, item.RowVersion);
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public StubCurrentUser(Guid userId) => UserId = userId;

        public bool IsAuthenticated => true;

        public Guid? UserId { get; }

        public string? UserCode => "tester";

        public string? UserName => "tester";

        public string? OrganizationCode => null;

        public IReadOnlyList<string> Roles => Array.Empty<string>();

        public bool HasRole(string roleCode) => false;

        public bool HasAnyRole(params string[] roleCodes) => false;
    }
}

/// <summary>审计测试实体。</summary>
public sealed class AuditTestItem : AuditableEntity<Guid>
{
    public string Name { get; set; } = string.Empty;
}
