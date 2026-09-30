using System;
using System.Threading.Tasks;
using CloudL.Domain.DomainEvents;
using CloudL.Domain.Entities;
using CloudL.Domain.Repositories;
using CloudL.SqlSugar;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// DI 布线回归：模块注册出来的东西必须**真的能用**。
/// </summary>
/// <remarks>
/// 今天发生过一次静默失效：仓储需要具体类型 <c>SqlSugarUnitOfWork</c>，而模块只注册了接口
/// → DI 注入 null → <strong>领域事件永远不被收集，且毫无报错</strong>。
/// 当时是靠走 DI 的测试抓到的，这条测试把那个保障固定下来。
/// </remarks>
public class SqlSugarDiWiringTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _provider;

    public SqlSugarDiWiringTests()
    {
        var name = "mem_" + Guid.NewGuid().ToString("N");
        var connectionString = $"DataSource=file:{name}?mode=memory&cache=shared";

        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventDispatcher, CapturingDispatcher>();
        services.AddCloudLSqlSugar(options =>
        {
            options.ConnectionString = connectionString;
            options.DbType = "Sqlite";
        });

        _provider = services.BuildServiceProvider();

        _provider.GetRequiredService<ISqlSugarClient>().CodeFirst.InitTables<DiWiringItem>();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _keepAlive.Dispose();
    }

    [Fact]
    public void Module_ShouldRegisterBothContractsAndShareOneUnitOfWork()
    {
        using var scope = _provider.CreateScope();

        var repository = scope.ServiceProvider.GetService<IRepository<DiWiringItem, Guid>>();
        var sqlSugarRepository = scope.ServiceProvider.GetService<ISqlSugarRepository<DiWiringItem, Guid>>();
        var unitOfWork = scope.ServiceProvider.GetService<IUnitOfWork>();
        var concrete = scope.ServiceProvider.GetService<SqlSugarUnitOfWork>();

        Assert.NotNull(repository);
        Assert.NotNull(sqlSugarRepository);
        Assert.NotNull(unitOfWork);
        Assert.NotNull(concrete);

        // 具体类型与接口必须指向同一实例，否则事件收集与事务会脱节
        Assert.Same(unitOfWork, concrete);
    }

    [Fact]
    public async Task Repository_ResolvedFromDi_ShouldCollectDomainEvents()
    {
        using var scope = _provider.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IRepository<DiWiringItem, Guid>>();

        var item = new DiWiringItem { Name = "di" };
        item.Touch();

        await repository.AddAsync(item);

        var dispatcher = (CapturingDispatcher)_provider.GetRequiredService<IDomainEventDispatcher>();

        Assert.Single(dispatcher.Events);
    }
}

/// <summary>DI 布线测试用实体。</summary>
public sealed class DiWiringItem : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;

    public void Touch() => AddDomainEvent(new DiWiringTouched());
}

/// <summary>DI 布线测试用事件。</summary>
public sealed record DiWiringTouched : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.Now;
}
