using CloudL.Domain.Entities;

namespace TemplateProject.Domain.Entities;

/// <summary>
/// 角色实体。
/// </summary>
public class Role : AuditableEntity<Guid>
{
    /// <summary>角色编码（唯一），取值见 <c>RoleCode</c>。</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>角色名称（唯一）。</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>角色描述。</summary>
    public string? Description { get; private set; }

    /// <summary>关联的用户-角色。</summary>
    public ICollection<UserRole> UserRoles { get; private set; } = new List<UserRole>();

    /// <summary>EF Core 物化用。</summary>
    private Role()
    {
    }

    public Role(string code, string name, string? description = null)
        : base(Guid.NewGuid())
    {
        Code = code;
        Name = name;
        Description = description;
    }

    /// <summary>更新角色信息。</summary>
    public void Update(string name, string? description)
    {
        Name = name;
        Description = description;
        MarkAsUpdated();
    }
}
