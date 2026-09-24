using CloudL.EntityFrameworkCore;
using CloudL.EntityFrameworkCore.EntityConfigurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateProject.Domain.Entities;

namespace TemplateProject.EntityFrameworkCore.EntityConfigurations;

/// <summary>
/// 用户实体配置。
/// 继承框架的 <see cref="AuditableEntityConfiguration{TEntity}"/> 后，
/// id / created_at / row_version / updated_at / created_by / updated_by 已由框架配置好，
/// 这里只需声明表名、业务字段与索引。
/// </summary>
public class UserConfiguration : AuditableEntityConfiguration<User>
{
    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<User> builder)
    {
        base.Configure(builder);

        builder.ToTable("users");

        builder.Property(user => user.Code)
            .HasColumnName("code")
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(user => user.UserName)
            .HasColumnName("user_name")
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(user => user.Email)
            .HasColumnName("email")
            .HasMaxLength(256);

        builder.Property(user => user.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(32);

        builder.Property(user => user.PasswordHash)
            .HasColumnName("password_hash")
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(user => user.Status)
            .HasColumnName("status")
            .HasConversion<int>();

        builder.Property(user => user.OrganizationCode)
            .HasColumnName("organization_code")
            .HasMaxLength(32);

        // 唯一约束
        builder.HasIndex(user => user.Code)
            .HasDatabaseName("ux_users_code")
            .IsUnique();

        builder.HasIndex(user => user.UserName)
            .HasDatabaseName("ux_users_user_name")
            .IsUnique();

        // 可空列的唯一索引：PostgreSQL 与 SQL Server 均允许多行 NULL
        builder.HasIndex(user => user.Email)
            .HasDatabaseName("ux_users_email")
            .IsUnique();

        builder.HasIndex(user => user.PhoneNumber)
            .HasDatabaseName("ux_users_phone_number")
            .IsUnique();
    }
}
