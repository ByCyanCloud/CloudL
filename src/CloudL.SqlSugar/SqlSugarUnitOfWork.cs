using System;
using System.Threading;
using System.Threading.Tasks;
using CloudL.Domain.Repositories;
using SqlSugar;

namespace CloudL.SqlSugar;

/// <summary>
/// 基于 SqlSugar 的工作单元实现。
/// </summary>
/// <remarks>
/// <para><strong>与 EF 版的固有差异（很重要）</strong>：SqlSugar 的仓储方法是<strong>立即执行</strong>的
/// （<c>Insertable/Updateable/Deleteable.ExecuteCommandAsync</c> 直接下发 SQL），<strong>没有变更跟踪</strong>，
/// 因此不存在 EF 那种"先攒着、等 SaveChanges 一起提交"的模型：</para>
/// <list type="bullet">
///   <item>EF：<c>AddAsync</c> 只入跟踪器，不写库；不调用 <c>SaveChangesAsync</c> 就不会落库。</item>
///   <item>SqlSugar：<c>AddAsync</c> <strong>已经落库</strong>；<see cref="SaveChangesAsync"/> 无待办事项，返回 0。</item>
/// </list>
/// <para>所以本实现下<strong>回滚只能靠事务</strong>（<see cref="ExecuteInTransactionAsync(Func{CancellationToken, Task}, CancellationToken)"/>）——
/// 请把一组需要同生共死的写操作放进事务里，而不要依赖"不调用 SaveChanges 就不会写"。</para>
/// <para>事务语义与 EF 版对齐：嵌套调用复用最外层事务（只有最外层决定提交或回滚）；异常时回滚并原样抛出。
/// 与 EF 的执行策略不同，这里<strong>不会重放</strong>操作委托。</para>
/// </remarks>
public class SqlSugarUnitOfWork : IUnitOfWork
{
    private readonly ISqlSugarClient _client;
    private int _transactionDepth;

    /// <summary>构造工作单元。</summary>
    public SqlSugarUnitOfWork(ISqlSugarClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
    }

    /// <inheritdoc />
    /// <remarks>SqlSugar 下写操作已在仓储方法内即时执行，故本方法没有待提交内容，固定返回 0。</remarks>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

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

    /// <summary>事务执行：嵌套时复用最外层事务，只有最外层负责提交或回滚。</summary>
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

            if (outermost)
                await _client.Ado.CommitTranAsync().ConfigureAwait(false);

            return result;
        }
        catch
        {
            _transactionDepth--;

            if (outermost)
                await _client.Ado.RollbackTranAsync().ConfigureAwait(false);

            throw;
        }
    }
}
