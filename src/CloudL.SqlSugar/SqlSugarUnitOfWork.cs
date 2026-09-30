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
    /// <para><strong>与 EF 版的固有差异（很重要）</strong>：SqlSugar 的仓储方法是<strong>立即执行</strong>的，
    /// <strong>没有变更跟踪</strong>，因此不存在"先攒着、等 SaveChanges 一起提交"的模型：
    /// EF 的 <c>AddAsync</c> 只入跟踪器、不写库，而这里的 <c>AddAsync</c> <strong>已经落库</strong>；
    /// <see cref="SaveChangesAsync"/> 没有待办事项，固定返回 0。</para>
    /// <para>所以本实现下<strong>回滚只能靠事务</strong>（<see cref="ExecuteInTransactionAsync(Func{CancellationToken, Task}, CancellationToken)"/>）——
    /// 请把一组需要同生共死的写操作放进事务里，而不要依赖"不调用 SaveChanges 就不会写"。</para>
    /// <para><strong>领域事件</strong>：写操作<strong>成功之后</strong>才收集；处于事务内时先攒起来、
    /// <strong>提交后</strong>才分发；回滚则<strong>丢弃</strong>（不会出现"通知发出去了、数据却没落库"的幽灵事件）。</para>
    /// <para><strong>乐观锁</strong>：支持带期望令牌的冲突检测 —— 用
    /// <c>SqlSugarRepository.UpdateAsync(entity, expectedRowVersion)</c>（不带令牌的重载只推进令牌、不做检测）。
    /// SqlSugar 没有变更跟踪、拿不到"原始令牌"，因此期望令牌需由调用方显式传入。</para>
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

        // 深度只在 finally 里递减一次：提交本身也可能抛（连接断开/死锁/超时），
        // 若在成功路径与 catch 里各减一次，深度会变负、outermost 永远为 false，
        // 于是 BeginTran 再也不执行 —— 写操作会静默脱离事务（2026-09-29 修复）。
        try
        {
            var result = await body().ConfigureAwait(false);

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
            if (outermost)
            {
                // 回滚：丢弃攒下的事件，避免幽灵事件。
                // 回滚自身失败不能掩盖原始异常，因此吞掉回滚异常。
                _pendingDomainEvents.Clear();

                try
                {
                    await _client.Ado.RollbackTranAsync().ConfigureAwait(false);
                }
                catch
                {
                    // 忽略回滚异常：原始异常更重要
                }
            }

            throw;
        }
        finally
        {
            _transactionDepth--;
        }
    }

    private async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        if (_dispatcher is null || domainEvents.Count == 0)
            return;

        await _dispatcher.DispatchAsync(domainEvents, cancellationToken).ConfigureAwait(false);
    }
}
