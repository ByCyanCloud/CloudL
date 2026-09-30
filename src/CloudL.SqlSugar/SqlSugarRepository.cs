using System.Reflection;
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CloudL.Domain.DomainEvents;
using CloudL.Domain.Shared.Exceptions;
using CloudL.Domain.Entities;
using CloudL.Domain.Repositories;
using CloudL.Domain.Shared.Time;
using SqlSugar;

namespace CloudL.SqlSugar;

/// <summary>
/// 基于 SqlSugar 的通用仓储实现。
/// </summary>
/// <remarks>
/// <para>与 <c>EfCoreRepository</c> <strong>语义对齐</strong>的点：分页 <c>pageIndex</c> 为 <strong>1 基</strong>；
/// 统计总数在排序之前完成；默认按 <c>CreatedAt</c> 排序；<strong>两个方向都追加主键作为次级排序键</strong>
/// （并列值之间的顺序在 SQL 中未定义，少了它会翻页重复或漏行）。</para>
/// <para><strong>与 EF 的固有差异</strong>：SqlSugar 没有变更跟踪，因此
/// <see cref="FindSingleForUpdateAsync"/> 与普通查询一致（取出实体、修改后显式 <see cref="UpdateAsync(TEntity, CancellationToken)"/>）。</para>
/// <para><strong>领域事件</strong>：写操作<strong>成功后</strong>把实体上的事件交给 <see cref="SqlSugarUnitOfWork"/>；
/// 不在事务内则立刻分发，在事务内则等提交后分发、回滚则丢弃。</para>
/// <para><strong>乐观锁</strong>：<see cref="UpdateAsync(TEntity, Guid, CancellationToken)"/> 会按期望令牌做条件更新，
/// 受影响行数为 0 时抛并发冲突（与 EF 的 <c>DbUpdateConcurrencyException</c> 语义对应）；
/// 无参重载只推进令牌、不做冲突检测。</para>
/// </remarks>
public class SqlSugarRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : Entity<TKey>, new()
    where TKey : notnull
{
    /// <summary>SqlSugar 客户端。</summary>
    protected ISqlSugarClient Client { get; }

    /// <summary>工作单元：用于收集领域事件（未注入时事件不会分发，且事务内也不会延迟）。</summary>
    protected SqlSugarUnitOfWork? UnitOfWork { get; }

    /// <summary>构造仓储。</summary>
    public SqlSugarRepository(ISqlSugarClient client, SqlSugarUnitOfWork? unitOfWork = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        Client = client;
        UnitOfWork = unitOfWork;
    }

    /// <summary>实体查询对象。</summary>
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

        return await Queryable.Where(predicate).SingleAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>SqlSugar 无变更跟踪，语义与 <see cref="FindSingleAsync"/> 相同；修改后请显式调用 <see cref="UpdateAsync(TEntity, CancellationToken)"/>。</remarks>
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

        await CollectEventsAsync(entity, cancellationToken).ConfigureAwait(false);

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

        foreach (var item in list)
        {
            await CollectEventsAsync(item, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>推进乐观锁令牌但不做冲突检测；需要检测请用 <see cref="UpdateAsync(TEntity, Guid, CancellationToken)"/>。</remarks>
    public virtual async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        RefreshAuditAndToken(entity);

        await Client.Updateable(entity).ExecuteCommandAsync().ConfigureAwait(false);

        await CollectEventsAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 按<strong>期望的乐观锁令牌</strong>更新：只有库里当前令牌等于 <paramref name="expectedRowVersion"/> 时才更新。
    /// </summary>
    /// <remarks>
    /// SqlSugar 没有变更跟踪，拿不到"原始令牌"，因此由调用方显式传入（通常来自查询到的实体）。
    /// 受影响行数为 0 说明数据已被他人修改 —— 抛并发冲突并不再分发领域事件。
    /// </remarks>
    public virtual async Task UpdateAsync(
        TEntity entity,
        Guid expectedRowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        RefreshAuditAndToken(entity);

        var affected = await Client.Updateable(entity)
            .Where(it => it.RowVersion == expectedRowVersion)
            .ExecuteCommandAsync()
            .ConfigureAwait(false);

        if (affected == 0)
        {
            throw new ConcurrencyConflictException("数据已被其他用户修改，请刷新后重试");
        }

        await CollectEventsAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        await Client.Deleteable(entity).ExecuteCommandAsync().ConfigureAwait(false);

        await CollectEventsAsync(entity, cancellationToken).ConfigureAwait(false);
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

    /// <summary>刷新审计时间与乐观锁令牌（EF 侧由 ApplyAuditFields 统一处理）。</summary>
    private static void RefreshAuditAndToken(TEntity entity)
    {
        NormalizeWallClock(entity);
        if (entity is IAuditable auditable)
        {
            auditable.UpdatedAt = CloudLTime.Now();
        }

        entity.RowVersion = Guid.NewGuid();
    }

    /// <summary>把实体上待分发的领域事件交给工作单元（并清空实体上的事件，避免重复分发）。</summary>
    private async Task CollectEventsAsync(TEntity entity, CancellationToken cancellationToken)
    {
        if (UnitOfWork is null)
        {
            entity.ClearDomainEvents();
            return;
        }

        var events = entity.DomainEvents.ToArray();

        entity.ClearDomainEvents();

        await UnitOfWork.CollectAsync(events, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 插入前补齐主键：SqlSugar 不会自动生成主键，键为默认值时所有行会互相覆盖。
    /// Guid 主键由框架补齐；其它类型请在建实体时传入主键，否则给出明确错误。
    /// </summary>
    private static void EnsureKey(TEntity entity)
    {
        NormalizeWallClock(entity);   // 墙上钟守卫（与 EF 侧同一意图）
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

    /// <summary>
    /// 墙上钟守卫：库里不存时区，<c>Kind != Unspecified</c> 的 <c>DateTime</c> 先换算成墙上钟。
    /// 与 EF 侧 <c>FrameworkDbContext</c> 的守卫同一意图 —— 否则写 PostgreSQL 会直接抛异常（表现为 500），
    /// 写 SqlServer/Sqlite 则会静默存入口径错误的时间。属性清单按类型缓存，避免每次写入都反射扫描。
    /// </summary>
    private static void NormalizeWallClock(TEntity entity)
    {
        var properties = WallClockProperties.GetOrAdd(typeof(TEntity), static type =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => (property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?))
                                   && property is { CanRead: true, CanWrite: true })
                .ToArray());

        foreach (var property in properties)
        {
            if (property.GetValue(entity) is DateTime value && value.Kind != DateTimeKind.Unspecified)
            {
                property.SetValue(entity, CloudLTime.ToWallClock(value));
            }
        }
    }

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> WallClockProperties = new();
}
