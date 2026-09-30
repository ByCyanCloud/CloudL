using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudL.Domain.DomainEvents;
using CloudL.Domain.Repositories;
using SqlSugar;

namespace CloudL.SqlSugar;

/// <summary>
/// 基于 SqlSugar 的工作单元实现，含<strong>领域事件</strong>的收集、延迟与提交后分发。
/// </summary>
/// <remarks>
/// <para><strong>与 EF 版的固有差异（很重要）</strong>：SqlSugar 的仓储方法是<strong>立即执行</strong>的
/// （<c>Insertable/Updateable/Deleteable.ExecuteCommandAsync</c> 直接下发 SQL），<strong>没有变更跟踪</strong>，
/// 因此不存在 EF 那种"先攒着、等 SaveChanges 一起提交"的模型：</para>
/// <list type="bullet">
///   <item>EF：<c>AddAsync</c> 只入跟踪器，不写库；不调用 <c>SaveChangesAsync</c> 就不会落库。</item>
///   <item>SqlSugar：<c>AddAsync</c> <strong>已经落库</strong>；<see cref="SaveChangesAsync"/> 无待办事项，返回 0。</item>
/// </list>
/// <para>所以本实现下<strong>回滚只能靠事务</strong> —— 请把一组需要同生共死的写操作放进事务里。</para>
/// <para><strong>领域事件语义与 EF 对齐</strong>：事件在写操作<strong>成功之后</strong>才分发；处于事务内时先攒起来，
/// <strong>提交后</strong>才分发；事务回滚则<strong>丢弃</strong>（不会出现"通知发出去了、数据却没落库"的幽灵事件）。</para>
/// <para><strong>乐观锁（RowVersion）尚未实现冲突检测</strong>：EF 依赖变更跟踪提供的"原始令牌"，
/// 而 SqlSugar 没有原值，因此本实现只在更新时<strong>推进</strong> RowVersion，不会检测陈旧写入。
/// 需要真冲突检测时应新增"显式传入期望令牌"的 API（列为后续版本待办）。</para>
/// </remarks>
public class SqlSugarUnitOfWork : IUnitOfWork
{
    private readonly ISqlSugarClient _client;
    private readonly IDomainEventDispatcher? _dispatcher;
    private readonly List<IDomainEvent> _pendingDomainEvents = [];
    private int _transactionDepth;

    /// <summary>构造工作单元。</summary>
    /// <param name="client">SqlSugar 客户端。</param>
    /// <param name="dispatcher">领域事件分发器（未注册时事件不会被分发，与 EF 版行为一致）。</param>
    public SqlSugarUnitOfWork(ISqlSugarClient client, IDomainEventDispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    /// <remarks>SqlSugar 下写操作已在仓储方法内即时执行，故本方法没有待提交内容，固定返回 0。</remarks>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    /// <summary>
    /// 收集实体上的领域事件（由仓储在写操作<strong>成功后</strong>调用）。
    /// </summary>
    /// <remarks>
    /// <para>实体上的事件会被<strong>立即清空</strong>（与 EF 版 <c>SaveChanges</c> 的 finally 一致），
    /// 这样即使后续失败也不会重复分发。</para>
    /// <para>不在事务内时<strong>立刻分发</strong>；在事务内则攒到提交之后再分发。</para>
    /// </remarks>
    internal async Task CollectAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        if (domainEvents is null)
            return;

        var collected = domainEvents.ToArray();

        if (collected.Length == 0)
            return;

        if (_transactionDepth > 0)
        {
            _pendingDomainEvents.AddRange(collected);
            return;
        }

        await DispatchAsync(collected, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return RunAsync(
            async () =>
            {
                await operation(cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return RunAsync(() => operation(cancellationToken), cancellationToken);
    }

    /// <summary>事务执行：嵌套时复用最外层事务，只有最外层负责提交/回滚与事件分发。</summary>
    private async Task<TResult> RunAsync<TResult>(Func<Task<TResult>> body, CancellationToken cancellationToken)
    {
        var outermost = _transactionDepth == 0;

        if (outermost)
            await _client.Ado.BeginTranAsync().ConfigureAwait(false);

        _transactionDepth++;

        try
        {
            var result = await body().ConfigureAwait(false);

            _transactionDepth--;

            if (!outermost)
                return result;

            await _client.Ado.CommitTranAsync().ConfigureAwait(false);

            // 提交成功之后才分发：回滚掉的工作不会产生任何事件
            var events = _pendingDomainEvents.ToArray();
            _pendingDomainEvents.Clear();

            await DispatchAsync(events, cancellationToken).ConfigureAwait(false);

            return result;
        }
        catch
        {
            _transactionDepth--;

            if (outermost)
            {
                // 回滚：丢弃攒下的事件，避免幽灵事件
                _pendingDomainEvents.Clear();

                await _client.Ado.RollbackTranAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        if (_dispatcher is null || domainEvents.Count == 0)
            return;

        await _dispatcher.DispatchAsync(domainEvents, cancellationToken).ConfigureAwait(false);
    }
}
