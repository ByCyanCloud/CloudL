namespace TemplateProject.Domain.Entities;

/// <summary>
/// 用户-角色关联实体（多对多中间表）。
/// 通过 <see cref="User.AssignRole"/> 创建，外部不应直接实例化。
/// </summary>
public class UserRole
{
    /// <summary>用户 ID。</summary>
    public Guid UserId { get; private set; }

    /// <summary>角色 ID。</summary>
    public Guid RoleId { get; private set; }

    /// <summary>用户导航属性。</summary>
    public User User { get; private set; } = null!;

    /// <summary>角色导航属性。</summary>
    public Role Role { get; private set; } = null!;

    /// <summary>EF Core 物化用。</summary>
    private UserRole()
    {
    }

    internal static UserRole Create(Guid userId, Guid roleId, Role role) => new()
    {
        UserId = userId,
        RoleId = roleId,
        Role = role
    };
}
