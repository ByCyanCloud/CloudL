using Livia.EntityFrameworkCore.EntityConfigurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateProject.Domain.Entities;

namespace TemplateProject.EntityFrameworkCore.EntityConfigurations;

/// <summary>
/// 角色实体配置。
/// </summary>
public class RoleConfiguration : AuditableEntityConfiguration<Role>
{
    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<Role> builder)
    {
        base.Configure(builder);

        builder.ToTable("roles");

        builder.Property(role => role.Code)
            .HasColumnName("code")
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(role => role.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(role => role.Description)
            .HasColumnName("description")
            .HasMaxLength(256);

        builder.HasIndex(role => role.Code)
            .HasDatabaseName("ux_roles_code")
            .IsUnique();

        builder.HasIndex(role => role.Name)
            .HasDatabaseName("ux_roles_name")
            .IsUnique();
    }
}
