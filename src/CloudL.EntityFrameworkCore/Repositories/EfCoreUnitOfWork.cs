using CloudL.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CloudL.EntityFrameworkCore.Repositories;

/// <summary>
/// 基于 EF Core 的工作单元实现。
/// 通过 <see cref="FrameworkDbContext"/> 提交，因此审计字段、乐观锁与领域事件分发对
/// <c>SaveChanges</c> / <c>SaveChangesAsync</c> 两条路径都生效。
/// </summary>
public class EfCoreUnitOfWork : IUnitOfWork
{
    private readonly FrameworkDbContext _dbContext;

    public EfCoreUnitOfWork(FrameworkDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _dbContext.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ExecuteInTransactionAsync(
            async token =>
            {
                await operation(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // 已经处在事务中（嵌套调用）→ 直接执行，提交或回滚交给最外层决定
        if (_dbContext.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        // 必须走执行策略：启用 EnableRetryOnFailure 时，手动事务不放进策略会在运行时抛异常
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // 事务范围内延迟领域事件分发（回滚时直接丢弃，不会产生幽灵事件）
            using var deferral = _dbContext.BeginDomainEventDeferral();

            await using var transaction = await _dbContext.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            TResult result;

            try
            {
                result = await operation(cancellationToken).ConfigureAwait(false);

                // 收尾：把操作里没有显式提交的变更一起落库
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    // 提交阶段失败时事务可能已经结束；此时回滚会抛"事务已完成"。
                    // 原始异常才是要向上抛的那个，因此这里不覆盖它。
                }

                _dbContext.DiscardDeferredDomainEvents();
                throw;
            }

            // 事务已提交，此时才分发领域事件
            await _dbContext.FlushDeferredDomainEventsAsync(cancellationToken).ConfigureAwait(false);

            return result;
        }).ConfigureAwait(false);
    }
}
