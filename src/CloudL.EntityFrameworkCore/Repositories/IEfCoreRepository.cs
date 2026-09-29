using System.Linq.Expressions;
using CloudL.Domain.Entities;
using CloudL.Domain.Repositories;

namespace CloudL.EntityFrameworkCore.Repositories;

/// <summary>
/// EF Core 专属仓储契约：在通用 <see cref="IRepository{TEntity, TKey}"/> 之上，
/// 额外暴露基于 <see cref="IQueryable{T}"/> 的"逃生口"。
/// </summary>
/// <remarks>
/// <para><strong>为什么要单独分层</strong>：这两个成员依赖 <strong>LINQ 提供程序</strong>，
/// 除 EF Core 之外的数据访问实现（例如 SqlSugar 的 <c>ISugarQueryable</c>）<strong>无法实现</strong>
/// <see cref="IQueryable{T}"/> —— 放进通用契约会让"通用仓储"绑死 EF。</para>
/// <para><strong>继承关系</strong>：本接口<strong>继承</strong> <see cref="IRepository{TEntity, TKey}"/>，
/// 因此业务仓储接口继承它以后，<strong>两套能力同时具备</strong>：</para>
/// <code>
/// // 只用中立能力（推荐：将来换 ORM 无痛）
/// public interface IUserRepository : IRepository&lt;User, Guid&gt; { }
///
/// // 需要 IQueryable 逃生口（EF 项目）
/// public interface IUserRepository : IEfCoreRepository&lt;User, Guid&gt; { }
/// </code>
/// <para>实现方由 <c>AddCloudLEntityFrameworkCore</c> 自动注册（<c>IEfCoreRepository&lt;,&gt;</c> 与
/// <c>IRepository&lt;,&gt;</c> 指向同一个 <see cref="EfCoreRepository{TEntity, TKey}"/> 实例类型）。</para>
/// </remarks>
public interface IEfCoreRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : Entity<TKey>
    where TKey : notnull
{
    /// <summary>获取可查询对象（无跟踪，用于投影查询）。</summary>
    IQueryable<TEntity> GetQueryable();

    /// <summary>执行投影查询。</summary>
    Task<List<TResult>> ToListAsync<TResult>(
        IQueryable<TResult> query,
        CancellationToken cancellationToken = default);
}
