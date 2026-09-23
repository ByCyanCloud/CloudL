using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateProject.Domain.Entities;

namespace TemplateProject.EntityFrameworkCore.EntityConfigurations;

/// <summary>
/// 用户-角色关联配置（复合主键）。
/// </summary>
public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");

        builder.HasKey(userRole => new { userRole.UserId, userRole.RoleId })
            .HasName("pk_user_roles");

        builder.Property(userRole => userRole.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(userRole => userRole.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        builder.HasOne(userRole => userRole.User)
            .WithMany(user => user.UserRoles)
            .HasForeignKey(userRole => userRole.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(userRole => userRole.Role)
            .WithMany(role => role.UserRoles)
            .HasForeignKey(userRole => userRole.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
