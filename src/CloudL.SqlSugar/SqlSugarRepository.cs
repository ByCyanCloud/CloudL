using CloudL.Domain.Shared.Time;
using System.Linq.Expressions;
using CloudL.Domain.Entities;
using CloudL.Domain.Repositories;
using SqlSugar;

namespace CloudL.SqlSugar;

/// <summary>
/// 基于 SqlSugar 的通用仓储实现。
/// </summary>
/// <remarks>
/// <para>与 <c>EfCoreRepository</c> <strong>语义对齐</strong>的点：分页 <c>pageIndex</c> 为 <strong>1 基</strong>；
/// 统计总数在排序之前完成；默认按 <c>CreatedAt</c> 排序；<strong>两个方向都追加主键作为次级排序键</strong>
/// （并列值之间的顺序在 SQL 中未定义，少了它会翻页重复或漏行）。</para>
/// <para><strong>与 EF 的固有差异</strong>（会在文档里说明）：SqlSugar 没有变更跟踪，
/// 因此"用于更新的跟踪查询"（<see cref="FindSingleForUpdateAsync"/>）在实现上与普通查询一致 ——
/// 取出实体、修改后由调用方显式调用 <see cref="UpdateAsync"/>（工作单元在事务结束时提交）。</para>
/// </remarks>
public class SqlSugarRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : Entity<TKey>, new()
    where TKey : notnull
{
    /// <summary>SqlSugar 客户端。</summary>
    protected ISqlSugarClient Client { get; }

    /// <summary>构造仓储。</summary>
    public SqlSugarRepository(ISqlSugarClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        Client = client;
    }

    /// <summary>实体查询对象（无跟踪概念，SqlSugar 按需即时查询）。</summary>
    protected ISugarQueryable<TEntity> Queryable => Client.Queryable<TEntity>();

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
        => await Client.Queryable<TEntity>().InSingleAsync(id).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return await Queryable.Where(predicate).ToListAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> FindSingleAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return await Queryable.Where(predicate).FirstAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>SqlSugar 无变更跟踪，语义与 <see cref="FindSingleAsync"/> 相同；修改后请显式调用 <see cref="UpdateAsync"/>。</remarks>
    public virtual Task<TEntity?> FindSingleForUpdateAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => FindSingleAsync(predicate, cancellationToken);

    /// <inheritdoc />
    public virtual async Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return await Queryable.Where(predicate).AnyAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        EnsureKey(entity);

        await Client.Insertable(entity).ExecuteCommandAsync().ConfigureAwait(false);

        return entity;
    }

    /// <inheritdoc />
    public virtual async Task AddRangeAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var list = entities as ICollection<TEntity> ?? entities.ToList();

        if (list.Count == 0)
            return;

        foreach (var item in list)
        {
            EnsureKey(item);
        }

        await Client.Insertable(list.ToList()).ExecuteCommandAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // 刷新审计时间：EF 侧由 FrameworkDbContext.ApplyAuditFields 统一处理，SqlSugar 没有那一步
        // 刷新审计时间：EF 由 ApplyAuditFields 统一处理，SqlSugar 没有那一步。
        // 用 IAuditable 接口是为了一次覆盖 AuditableEntity<TKey> 与非泛型 AuditableEntity 两个基类。
        if (entity is IAuditable auditable)
            auditable.UpdatedAt = CloudLTime.Now();

        await Client.Updateable(entity).ExecuteCommandAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        await Client.Deleteable(entity).ExecuteCommandAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task DeleteByIdAsync(TKey id, CancellationToken cancellationToken = default)
        => await Client.Deleteable<TEntity>().In(id).ExecuteCommandAsync().ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
        => predicate is null
            ? await Queryable.CountAsync().ConfigureAwait(false)
            : await Queryable.Where(predicate).CountAsync().ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<PagedResult<TEntity>> GetPagedAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<TEntity, bool>>? predicate = null,
        Expression<Func<TEntity, object>>? orderBy = null,
        bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var query = Queryable;

        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        // 与 EF 版一致：总数在排序之前统计
        var totalCount = await query.CountAsync().ConfigureAwait(false);

        orderBy ??= entity => entity.CreatedAt;

        // 与 EF 版一致：两个方向都追加主键作为次级排序键（升序），避免并列值时翻页重复或漏行
        query = query
            .OrderBy(orderBy, descending ? OrderByType.Desc : OrderByType.Asc)
            .OrderBy(entity => entity.Id, OrderByType.Asc);

        // 与 EF 版一致：pageIndex 为 1 基
        var items = await query
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync()
            .ConfigureAwait(false);

        return new PagedResult<TEntity>
        {
            Items = items,
            TotalCount = totalCount,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }


    /// <summary>
    /// 插入前补齐主键：SqlSugar 不会自动生成主键，键为默认值时所有行会互相覆盖。
    /// Guid 主键由框架补齐；其它类型请在建实体时传入主键，否则给出明确错误。
    /// </summary>
    private static void EnsureKey(TEntity entity)
    {
        if (!EqualityComparer<TKey>.Default.Equals(entity.Id, default!))
            return;

        if (typeof(TKey) == typeof(Guid))
        {
            entity.AssignId((TKey)(object)Guid.NewGuid());
            return;
        }

        throw new InvalidOperationException(
            $"实体 {typeof(TEntity).Name} 的主键（{typeof(TKey).Name}）没有生成：SqlSugar 不会自动生成主键，" +
            "请在构造实体时传入主键，或改用 Guid 主键（框架会补齐）。");
    }
}
