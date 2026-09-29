namespace CloudL.Domain.Shared.Persistence;

/// <summary>
/// 当前应用启用的持久化实现种类。
/// </summary>
public enum PersistenceProviderKind
{
    /// <summary>CloudL.EntityFrameworkCore（EF Core 实现）。</summary>
    EntityFrameworkCore,

    /// <summary>CloudL.SqlSugar（SqlSugar 实现，适用于达梦等国产数据库）。</summary>
    SqlSugar
}

/// <summary>
/// 持久化实现的中立标记：由各持久化模块在注册时写入，用于<strong>互斥检查</strong>。
/// </summary>
/// <remarks>
/// <para><strong>规则</strong>：同一个应用只允许启用<strong>一套</strong>持久化实现 —— 两套并存会出现
/// 两套审计、事务、领域事件与迁移语义，导致数据"有时对有时不对"（详见 CONTRACT.md）。</para>
/// <para>因此 <c>AddCloudLEntityFrameworkCore</c> 与 <c>AddCloudLSqlSugar</c> 都会在注册前检查本标记，
/// 发现已存在<strong>另一种</strong>实现时<strong>立即抛异常</strong>（启动即失败），把一个难以排查的
/// 数据不一致问题变成一眼可见的配置错误。</para>
/// <para>本类型刻意放在 <c>CloudL.Core</c>：两个持久化包都引用它，因此彼此<strong>不需要互相引用</strong>。</para>
/// </remarks>
/// <param name="Kind">已启用的实现种类。</param>
public sealed record PersistenceProvider(PersistenceProviderKind Kind);
