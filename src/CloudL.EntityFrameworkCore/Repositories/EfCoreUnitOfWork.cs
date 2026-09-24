using CloudL.Domain.Repositories;

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
}