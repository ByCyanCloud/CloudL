using System.Linq.Expressions;
using CloudL.Domain.Entities;
using CloudL.Domain.Repositories;
using CloudL.Domain.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace CloudL.EntityFrameworkCore.Repositories;

/// <summary>
/// 基于 EF Core 的通用仓储实现。
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <typeparam name="TKey">主键类型。</typeparam>
public class EfCoreRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : Entity<TKey>
    where TKey : notnull
{
    /// <summary>当前 DbContext。</summary>
    protected FrameworkDbContext DbContext { get; }

    /// <summary>当前实体的 DbSet。</summary>
    protected DbSet<TEntity> DbSet { get; }

    public EfCoreRepository(FrameworkDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        DbContext = dbContext;
        DbSet = dbContext.Set<TEntity>();
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
        => await DbSet.FindAsync([id], cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking()
            .Where(predicate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<TEntity?> FindSingleAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking()
            .SingleOrDefaultAsync(predicate, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<TEntity?> FindSingleForUpdateAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await DbSet.SingleOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await DbSet.AnyAsync(predicate, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        var entry = await DbSet.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        return entry.Entity;
    }

    /// <inheritdoc />
    public virtual Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        => DbSet.AddRangeAsync(entities, cancellationToken);

    /// <inheritdoc />
    public virtual Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        DbSet.Update(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        DbSet.Remove(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual async Task DeleteByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (entity is not null)
        {
            DbSet.Remove(entity);
        }
    }

    /// <inheritdoc />
    public virtual async Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
        => predicate is null
            ? await DbSet.CountAsync(cancellationToken).ConfigureAwait(false)
            : await DbSet.CountAsync(predicate, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<PagedResult<TEntity>> GetPagedAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<TEntity, bool>>? predicate = null,
        Expression<Func<TEntity, object>>? orderBy = null,
        bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsNoTracking().AsQueryable();

        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        orderBy ??= entity => entity.CreatedAt;
        // 追加主键作为次级排序键：只按业务字段排序时，并列值之间的顺序在 SQL 中未定义，
        // 会导致翻页出现重复行或漏行。
        query = descending
            ? query.OrderByDescending(orderBy).ThenBy(entity => entity.Id)
            : query.OrderBy(orderBy).ThenBy(entity => entity.Id);

        var items = await query
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<TEntity>
        {
            Items = items,
            TotalCount = totalCount,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    /// <inheritdoc />
    public virtual IQueryable<TEntity> GetQueryable() => DbSet.AsNoTracking();

    /// <inheritdoc />
    public virtual Task<List<TResult>> ToListAsync<TResult>(
        IQueryable<TResult> query,
        CancellationToken cancellationToken = default)
        => query.ToListAsync(cancellationToken);
}