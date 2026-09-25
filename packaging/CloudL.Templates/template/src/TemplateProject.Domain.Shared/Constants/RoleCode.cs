namespace TemplateProject.Domain.Shared.Constants;

/// <summary>
/// 系统角色 Code（对应 roles 表的 code 字段）。
/// 角色命名与层级属于业务约定，因此定义在业务侧而不是框架里；
/// 框架只提供 <c>ICurrentUser.HasRole</c> / <c>HasAnyRole</c> 这类判断原语。
/// </summary>
public static class RoleCode
{
    /// <summary>普通用户。</summary>
    public const string User = "user";

    /// <summary>管理者。</summary>
    public const string Manager = "manager";

    /// <summary>管理员。</summary>
    public const string Admin = "admin";

    /// <summary>超级管理员。</summary>
    public const string Super = "super";

    /// <summary>全部角色。</summary>
    public static readonly IReadOnlyList<string> All = [User, Manager, Admin, Super];

    /// <summary>判断角色码是否合法。</summary>
    public static bool IsValid(string code) =>
        !string.IsNullOrEmpty(code) && All.Contains(code, StringComparer.Ordinal);
}
