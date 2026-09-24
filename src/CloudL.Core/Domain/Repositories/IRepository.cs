using System.Linq.Expressions;
using CloudL.Domain.Entities;

namespace CloudL.Domain.Repositories;

/// <summary>
/// 通用仓储接口，定义基础 CRUD 与分页查询能力。
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <typeparam name="TKey">主键类型。</typeparam>
public interface IRepository<TEntity, TKey>
    where TEntity : Entity<TKey>
    where TKey : notnull
{
    /// <summary>根据主键获取实体（跟踪查询）。</summary>
    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);

    /// <summary>获取全部实体（已限制最大条数）。</summary>
    [Obsolete("请使用 GetPagedAsync 分页查询，避免一次性加载全表数据。")]
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>按条件查询（无跟踪）。</summary>
    Task<IReadOnlyList<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>按条件查询单个实体（无跟踪）。</summary>
    Task<TEntity?> FindSingleAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>按条件查询单个实体（跟踪查询，用于后续更新）。</summary>
    Task<TEntity?> FindSingleForUpdateAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>判断是否存在满足条件的实体。</summary>
    Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>新增实体（不提交，由工作单元统一提交）。</summary>
    Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>批量新增（不提交）。</summary>
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <summary>更新实体（不提交）。</summary>
    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>删除实体（不提交）。</summary>
    Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>根据主键删除（不提交）。</summary>
    Task DeleteByIdAsync(TKey id, CancellationToken cancellationToken = default);

    /// <summary>统计条数。</summary>
    Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default);

    /// <summary>分页查询。</summary>
    Task<PagedResult<TEntity>> GetPagedAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<TEntity, bool>>? predicate = null,
        Expression<Func<TEntity, object>>? orderBy = null,
        bool descending = true,
        CancellationToken cancellationToken = default);

    /// <summary>获取可查询对象（无跟踪，用于投影查询）。</summary>
    IQueryable<TEntity> GetQueryable();

    /// <summary>执行投影查询。</summary>
    Task<List<TResult>> ToListAsync<TResult>(
        IQueryable<TResult> query,
        CancellationToken cancellationToken = default);
}