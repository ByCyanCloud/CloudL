namespace Livia.Application.Contracts.IServices;

/// <summary>
/// 当前请求的用户上下文，由 JWT Claims 解析而来。
/// 框架只提供角色判断原语（<see cref="HasRole"/> / <see cref="HasAnyRole"/>），
/// 角色层级与命名属于业务约定，由业务侧自行定义常量与授权策略。
/// </summary>
public interface ICurrentUser
{
    /// <summary>当前请求是否已通过认证。</summary>
    bool IsAuthenticated { get; }

    /// <summary>当前用户 ID。</summary>
    Guid? UserId { get; }

    /// <summary>当前用户编码。</summary>
    string? UserCode { get; }

    /// <summary>当前用户名。</summary>
    string? UserName { get; }

    /// <summary>所属机构编码。</summary>
    string? OrganizationCode { get; }

    /// <summary>当前用户的角色编码集合。</summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>是否拥有指定角色。</summary>
    bool HasRole(string roleCode);

    /// <summary>是否拥有其中任意一个角色。</summary>
    bool HasAnyRole(params string[] roleCodes);
}