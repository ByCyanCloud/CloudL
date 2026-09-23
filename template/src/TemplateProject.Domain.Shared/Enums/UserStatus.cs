namespace TemplateProject.Domain.Shared.Enums;

/// <summary>
/// 用户状态。
/// </summary>
public enum UserStatus
{
    /// <summary>未激活。</summary>
    Inactive = 0,

    /// <summary>正常。</summary>
    Active = 1,

    /// <summary>已锁定。</summary>
    Locked = 2,

    /// <summary>已禁用。</summary>
    Disabled = 3
}
