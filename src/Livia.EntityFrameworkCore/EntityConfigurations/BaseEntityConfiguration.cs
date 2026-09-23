using Livia.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Livia.EntityFrameworkCore.EntityConfigurations;

/// <summary>
/// 实体配置基类（泛型主键版本）。配置 Id、CreatedAt、RowVersion 三个基础字段。
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <typeparam name="TKey">主键类型。</typeparam>
public abstract class BaseEntityConfiguration<TEntity, TKey> : IEntityTypeConfiguration<TEntity>
    where TEntity : Entity<TKey>
    where TKey : notnull
{
    /// <inheritdoc />
    public virtual void Configure(EntityTypeBuilder<TEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(entity => entity.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(entity => entity.RowVersion)
            .HasColumnName("row_version")
            .IsConcurrencyToken();
    }
}

/// <summary>
/// 可审计实体配置基类（Guid 主键版本），适用于 User、Role 这类实体。
/// </summary>
public abstract class AuditableEntityConfiguration<TEntity> : BaseEntityConfiguration<TEntity, Guid>
    where TEntity : AuditableEntity<Guid>
{
    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<TEntity> builder)
    {
        base.Configure(builder);

        builder.Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        builder.Property(entity => entity.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired(false);

        builder.Property(entity => entity.UpdatedBy)
            .HasColumnName("updated_by")
            .IsRequired(false);
    }
}

/// <summary>
/// 可审计实体配置基类（泛型主键版本）。
/// </summary>
public abstract class AuditableEntityConfiguration<TEntity, TKey> : BaseEntityConfiguration<TEntity, TKey>
    where TEntity : AuditableEntity<TKey>
    where TKey : notnull
{
    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<TEntity> builder)
    {
        base.Configure(builder);

        builder.Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        builder.Property(entity => entity.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired(false);

        builder.Property(entity => entity.UpdatedBy)
            .HasColumnName("updated_by")
            .IsRequired(false);
    }
}

/// <summary>
/// 可审计实体配置基类（复合主键版本）。
/// 配置 CreatedAt、RowVersion 与审计字段，但<strong>不配置主键</strong>，由子类通过 HasKey 自行定义。
/// </summary>
public abstract class AuditableConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : Entity, IAuditable
{
    /// <inheritdoc />
    public virtual void Configure(EntityTypeBuilder<TEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(entity => entity.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(entity => entity.RowVersion)
            .HasColumnName("row_version")
            .IsConcurrencyToken();

        builder.Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        builder.Property(entity => entity.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired(false);

        builder.Property(entity => entity.UpdatedBy)
            .HasColumnName("updated_by")
            .IsRequired(false);
    }
}