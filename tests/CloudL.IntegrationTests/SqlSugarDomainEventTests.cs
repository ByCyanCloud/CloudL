using CloudL.Domain.Shared.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudL.Domain.DomainEvents;
using CloudL.Domain.Entities;
using CloudL.Domain.Shared.Exceptions;
using CloudL.SqlSugar;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// SqlSugar 第二批：领域事件（提交后分发、回滚丢弃）与乐观锁（期望令牌冲突检测）。
/// </summary>
public class SqlSugarDomainEventTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _provider;
    private readonly ISqlSugarClient _client;
    private readonly SqlSugarUnitOfWork _unitOfWork;
    private readonly SqlSugarRepository<EventTestItem, Guid> _repository;

    public SqlSugarDomainEventTests()
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
        _client = _provider.GetRequiredService<ISqlSugarClient>();
        _unitOfWork = _provider.GetRequiredService<SqlSugarUnitOfWork>();
        _repository = new SqlSugarRepository<EventTestItem, Guid>(_client, _unitOfWork);

        _client.CodeFirst.InitTables<EventTestItem>();
    }

    private CapturingDispatcher Dispatcher => (CapturingDispatcher)_provider.GetRequiredService<IDomainEventDispatcher>();

    public void Dispose()
    {
        _provider.Dispose();
        _keepAlive.Dispose();
    }

    [Fact]
    public async Task AddAsync_OutsideTransaction_ShouldDispatchImmediately()
    {
        var item = new EventTestItem { Name = "a" };
        item.Touch();

        await _repository.AddAsync(item);

        Assert.Single(Dispatcher.Events);
        Assert.Empty(item.DomainEvents);          // 实体上的事件已清空，不会重复分发
    }

    [Fact]
    public async Task Commit_ShouldDispatchAfterCommit()
    {
        await _unitOfWork.ExecuteInTransactionAsync(async _ =>
        {
            var item = new EventTestItem { Name = "b" };
            item.Touch();

            await _repository.AddAsync(item);

            // 事务还没提交，事件不应已经分发出去
            Assert.Empty(Dispatcher.Events);
        });

        Assert.Single(Dispatcher.Events);
        Assert.Equal(1, await _repository.CountAsync());
    }

    [Fact]
    public async Task Rollback_ShouldDiscardEvents_NoGhostEvents()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _unitOfWork.ExecuteInTransactionAsync(async _ =>
            {
                var item = new EventTestItem { Name = "ghost" };
                item.Touch();

                await _repository.AddAsync(item);

                throw new InvalidOperationException("模拟业务失败");
            }));

        // 数据没落库，事件也绝不能发出去
        Assert.Equal(0, await _repository.CountAsync());
        Assert.Empty(Dispatcher.Events);
    }

    [Fact]
    public async Task UpdateAsync_WithStaleToken_ShouldThrowAndNotDispatch()
    {
        var item = new EventTestItem { Name = "c" };
        await _repository.AddAsync(item);

        var loaded = await _repository.FindSingleAsync(x => x.Id == item.Id);
        Assert.NotNull(loaded);

        var staleToken = loaded!.RowVersion;

        // 别处改了一次，令牌前进
        loaded.Name = "c-updated";
        await _repository.UpdateAsync(loaded);

        // 用过期令牌再改：必须冲突
        var another = await _repository.FindSingleAsync(x => x.Id == item.Id);
        another!.Name = "c-stale";
        another.Touch();

        var before = Dispatcher.Events.Count;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            _repository.UpdateAsync(another, staleToken));

        Assert.Equal(before, Dispatcher.Events.Count);   // 冲突时不得分发事件
    }

    [Fact]
    public async Task UpdateAsync_WithCorrectToken_ShouldSucceedAndAdvanceToken()
    {
        var item = new EventTestItem { Name = "d" };
        await _repository.AddAsync(item);

        var loaded = await _repository.FindSingleAsync(x => x.Id == item.Id);
        Assert.NotNull(loaded);

        var token = loaded!.RowVersion;
        loaded.Name = "d-updated";

        await _repository.UpdateAsync(loaded, token);

        var reloaded = await _repository.FindSingleAsync(x => x.Id == item.Id);
        Assert.Equal("d-updated", reloaded!.Name);
        Assert.NotEqual(token, reloaded.RowVersion);      // 令牌已前进
    }
}

/// <summary>测试用领域事件。</summary>
public sealed record EventTestItemTouched : IDomainEvent
{
    /// <summary>发生时间。领域事件只在内存中流转、不入库，因此用带偏移量的 DateTimeOffset。</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.Now;
}

/// <summary>测试实体：通过受保护的 AddDomainEvent 登记事件。</summary>
public sealed class EventTestItem : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;

    public void Touch() => AddDomainEvent(new EventTestItemTouched());
}

/// <summary>捕获型分发器，用于断言"什么时候分发、分发了几条"。</summary>
public sealed class CapturingDispatcher : IDomainEventDispatcher
{
    private readonly List<IDomainEvent> _events = [];

    public IReadOnlyList<IDomainEvent> Events => _events;

    public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        _events.AddRange(domainEvents);

        return Task.CompletedTask;
    }
}
