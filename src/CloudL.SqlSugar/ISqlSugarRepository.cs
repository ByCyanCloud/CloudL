using System;
using System.Threading;
using System.Threading.Tasks;
using CloudL.Domain.Entities;
using CloudL.Domain.Repositories;

namespace CloudL.SqlSugar;

/// <summary>
/// SqlSugar 专属仓储契约：在通用 <see cref="IRepository{TEntity, TKey}"/> 之上，暴露
/// <strong>带期望令牌的乐观锁更新</strong>。
/// </summary>
/// <remarks>
/// <para><strong>为什么需要单独一个接口</strong>：冲突检测重载只存在于具体类上时，
/// 注入 <see cref="IRepository{TEntity, TKey}"/> 的调用方<strong>根本调不到它</strong>，
/// 于是只能退回不带检测的重载，<strong>静默失去并发保护</strong>。把它抽到接口上，
/// 需要冲突检测的代码就能显式依赖本接口（与 EF 侧 <c>IEfCoreRepository</c> 同一思路）。</para>
/// <para>SqlSugar 没有变更跟踪、拿不到"原始令牌"，因此期望令牌需由调用方显式传入
/// （通常来自刚查询到的实体）。</para>
/// </remarks>
public interface ISqlSugarRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : Entity<TKey>, new()
    where TKey : notnull
{
    /// <summary>
    /// 按<strong>期望的乐观锁令牌</strong>更新：只有库里当前令牌等于
    /// <paramref name="expectedRowVersion"/> 时才更新，影响 0 行则抛并发冲突。
    /// </summary>
    Task UpdateAsync(
        TEntity entity,
        Guid expectedRowVersion,
        CancellationToken cancellationToken = default);
}
