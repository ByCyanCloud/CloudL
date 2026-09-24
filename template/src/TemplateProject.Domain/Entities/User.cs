using CloudL.Domain.Aggregates;
using CloudL.Domain.Entities;
using TemplateProject.Domain.Events;
using TemplateProject.Domain.Shared.Enums;

namespace TemplateProject.Domain.Entities;

/// <summary>
/// 用户聚合根（示例业务实体）。
/// 继承框架的 <see cref="AuditableEntity{TKey}"/> 即可获得审计字段与乐观锁令牌，
/// 无需在本项目重复实现这些基础设施。
/// </summary>
public class User : AuditableEntity<Guid>, IAggregateRoot
{
    /// <summary>用户编码（唯一）。</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>用户名（唯一，用于登录）。</summary>
    public string UserName { get; private set; } = string.Empty;

    /// <summary>邮箱（可选，唯一）。</summary>
    public string? Email { get; private set; }

    /// <summary>手机号（可选，唯一）。</summary>
    public string? PhoneNumber { get; private set; }

    /// <summary>密码哈希（由 <c>IPasswordHasher</c> 生成，业务侧不关心算法细节）。</summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>用户状态。</summary>
    public UserStatus Status { get; private set; } = UserStatus.Active;

    /// <summary>所属机构编码。</summary>
    public string? OrganizationCode { get; private set; }

    /// <summary>用户-角色关联。</summary>
    public ICollection<UserRole> UserRoles { get; private set; } = new List<UserRole>();

    /// <summary>角色编码列表（用于写入 JWT Claims）。</summary>
    public IEnumerable<string> RoleCodes => UserRoles
        .Where(userRole => userRole.Role is not null)
        .Select(userRole => userRole.Role.Code);

    /// <summary>EF Core 物化用。</summary>
    private User()
    {
    }

    public User(
        string code,
        string userName,
        string passwordHash,
        string? email = null,
        string? phoneNumber = null,
        string? organizationCode = null)
        : base(Guid.NewGuid())
    {
        Code = code;
        UserName = userName;
        PasswordHash = passwordHash;
        Email = email;
        PhoneNumber = phoneNumber;
        OrganizationCode = organizationCode;
        Status = UserStatus.Active;

        AddDomainEvent(new UserRegisteredEvent(Id, UserName, Email));
    }

    /// <summary>分配角色（重复分配自动忽略）。</summary>
    public void AssignRole(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (UserRoles.Any(userRole => userRole.RoleId == role.Id))
            return;

        UserRoles.Add(UserRole.Create(Id, role.Id, role));
    }

    /// <summary>移除角色。</summary>
    public void RemoveRole(Guid roleId)
    {
        var userRole = UserRoles.FirstOrDefault(item => item.RoleId == roleId);
        if (userRole is not null)
        {
            UserRoles.Remove(userRole);
        }
    }

    /// <summary>更新可编辑资料。null 表示该字段保持不变。</summary>
    public void UpdateProfile(string? email, string? phoneNumber)
    {
        if (email is not null)
            Email = email;

        if (phoneNumber is not null)
            PhoneNumber = phoneNumber;

        MarkAsUpdated();
    }

    /// <summary>
    /// 更新密码哈希。
    /// 除改密场景外，登录时发现哈希参数偏弱（<c>IPasswordHasher.NeedsRehash</c>）也会调用此方法，
    /// 实现密码强度的透明升级。
    /// </summary>
    public void UpdatePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        PasswordHash = passwordHash;
        MarkAsUpdated();
    }

    /// <summary>修改状态。</summary>
    public void ChangeStatus(UserStatus status)
    {
        Status = status;
        MarkAsUpdated();
    }
}
