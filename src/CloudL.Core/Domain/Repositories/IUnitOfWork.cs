namespace CloudL.Domain.Repositories;

/// <summary>
/// 工作单元：统一提交事务边界。应用服务通过它提交仓储上的变更。
/// </summary>
public interface IUnitOfWork
{
    /// <summary>提交所有待保存的变更，返回受影响的行数。</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 在<strong>一个事务</strong>内执行操作：其中任一步失败则整体回滚。
    /// </summary>
    /// <remarks>
    /// <para><strong>领域事件在事务提交之后才分发</strong>，因此回滚掉的工作不会产生任何事件
    /// （不会出现"通知已经发出去、数据却没落库"的幽灵事件）。</para>
    /// <para>代价：事件处理器里的写操作属于<strong>新事务</strong>，不随主事务回滚。
    /// 若需要处理器与主事务同生共死，请把它显式写成主操作的一部分。</para>
    /// <para>嵌套调用会复用最外层事务；最外层决定提交或回滚。
    /// 操作结束后会自动调用一次 <see cref="SaveChangesAsync"/> 收尾。</para>
    /// </remarks>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在事务内执行操作并返回结果，语义与
    /// <see cref="ExecuteInTransactionAsync(Func{CancellationToken, Task}, CancellationToken)"/> 相同。
    /// </summary>
    /// <typeparam name="TResult">操作返回值类型。</typeparam>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
