namespace CloudL.Domain.Entities;

using CloudL.Domain.DomainEvents;
using CloudL.Domain.Shared.Time;

/// <summary>
/// 实体标记基类（非泛型），承载领域事件容器。
/// 所有实体（含泛型主键实体与复合主键实体）都继承此类。
/// </summary>
public abstract class BaseEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>当前实体上待分发的领域事件。</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>登记一个领域事件，将在 SaveChanges 成功后分发。</summary>
    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <summary>清空领域事件（由 DbContext 在分发完成后调用）。</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// 实体基类（非泛型，无 Id），适用于复合主键实体。
/// 提供 <see cref="CreatedAt"/> 与乐观锁令牌 <see cref="RowVersion"/>，主键由子类自行定义。
/// </summary>
public abstract class Entity : BaseEntity
{
    /// <summary>创建时间（按 <c>Time:Clock</c> 口径的墙上钟，<c>Kind=Unspecified</c>）。</summary>
    public DateTime CreatedAt { get; protected set; }

    /// <summary>并发令牌（乐观锁）。</summary>
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    protected Entity()
    {
        // 必须用 CloudLTime：框架不存储时区，时间列是 timestamp without time zone，
        // 写 Kind=Utc 会被 Npgsql 拒绝（Cannot write DateTime with Kind=UTC to ...）。
        CreatedAt = CloudLTime.Now();
    }
}

/// <summary>
/// 带泛型主键的实体基类。在 <see cref="Entity"/> 基础上增加 <see cref="Id"/> 与基于主键的相等性比较。
/// </summary>
/// <typeparam name="TKey">主键类型（Guid、int、string 等）。</typeparam>
public abstract class Entity<TKey> : Entity
    where TKey : notnull
{
    /// <summary>实体唯一标识。</summary>
    public TKey Id { get; protected set; } = default!;

    protected Entity()
    {
    }

    protected Entity(TKey id)
    {
        Id = id;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TKey> other)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (GetType() != other.GetType())
            return false;

        if (EqualityComparer<TKey>.Default.Equals(Id, default!)
            || EqualityComparer<TKey>.Default.Equals(other.Id, default!))
        {
            return false;
        }

        return EqualityComparer<TKey>.Default.Equals(Id, other.Id);
    }

    /// <inheritdoc />
    public override int GetHashCode() => Id?.GetHashCode() ?? 0;

    public static bool operator ==(Entity<TKey>? left, Entity<TKey>? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(Entity<TKey>? left, Entity<TKey>? right) =>
        !(left == right);
}
