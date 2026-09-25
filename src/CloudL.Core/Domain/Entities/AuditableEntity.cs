namespace CloudL.Domain.Entities;

/// <summary>
/// 可审计实体契约。<c>FrameworkDbContext</c> 依据此接口自动填充审计字段并推进乐观锁令牌。
/// </summary>
public interface IAuditable
{
    /// <summary>最后修改时间（UTC）。</summary>
    DateTime? UpdatedAt { get; set; }

    /// <summary>创建人 ID。</summary>
    Guid? CreatedBy { get; set; }

    /// <summary>最后修改人 ID。</summary>
    Guid? UpdatedBy { get; set; }

    /// <summary>并发令牌（乐观锁）。</summary>
    Guid RowVersion { get; set; }
}

/// <summary>
/// 可审计实体基类（泛型主键版本）。
/// </summary>
/// <typeparam name="TKey">主键类型。</typeparam>
public abstract class AuditableEntity<TKey> : Entity<TKey>, IAuditable
    where TKey : notnull
{
    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }

    protected AuditableEntity()
    {
    }

    protected AuditableEntity(TKey id)
        : base(id)
    {
    }

    /// <summary>标记实体已被修改（设置 UpdatedAt）。</summary>
    protected void MarkAsUpdated() => UpdatedAt = DateTime.UtcNow;

    /// <summary>设置创建人。</summary>
    protected void SetCreatedBy(Guid userId) => CreatedBy = userId;

    /// <summary>设置最后修改人。</summary>
    protected void SetUpdatedBy(Guid userId) => UpdatedBy = userId;
}

/// <summary>
/// 可审计实体基类（非泛型，无预设 Id），适用于复合主键的审计实体。
/// </summary>
public abstract class AuditableEntity : Entity, IAuditable
{
    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }

    protected AuditableEntity()
    {
    }

    /// <summary>标记实体已被修改（设置 UpdatedAt）。</summary>
    protected void MarkAsUpdated() => UpdatedAt = DateTime.UtcNow;

    /// <summary>设置创建人。</summary>
    protected void SetCreatedBy(Guid userId) => CreatedBy = userId;

    /// <summary>设置最后修改人。</summary>
    protected void SetUpdatedBy(Guid userId) => UpdatedBy = userId;
}
