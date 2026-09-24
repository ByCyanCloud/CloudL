namespace CloudL.Domain.Repositories;

/// <summary>
/// 工作单元：统一提交事务边界。应用服务通过它提交仓储上的变更。
/// </summary>
public interface IUnitOfWork
{
    /// <summary>提交所有待保存的变更，返回受影响的行数。</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}