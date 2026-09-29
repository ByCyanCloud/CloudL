using System;

namespace CloudL.Domain.Shared.Persistence;

/// <summary>
/// 标记"<strong>不属于数据库</strong>"的成员：它只存在于内存模型中，任何持久化实现都必须跳过它。
/// </summary>
/// <remarks>
/// <para><strong>为什么需要它</strong>：框架的实体基类里有一些非持久化成员（例如 <c>BaseEntity.DomainEvents</c>）。
/// EF Core 靠 fluent 配置里的 <c>Ignore(...)</c> 排除它们，而其它 ORM 读不到那份配置 ——
/// SqlSugar 就会把它当成一列去写，运行时直接抛
/// <c>No mapping exists from object type ... IDomainEvent</c>。</para>
/// <para>本属性是<strong>中立</strong>的：放在 <c>CloudL.Core</c>，各持久化实现按同一份声明行事，
/// 从而做到"<strong>忽略规则只写一份</strong>"。以后框架基类新增非持久化成员时，
/// 只要打上本属性，所有 ORM 都会跳过它，不会再出现"某个 ORM 才知道要忽略"的情况。</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public sealed class NotPersistedAttribute : Attribute
{
}
