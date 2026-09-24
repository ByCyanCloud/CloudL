using System.Security.Claims;
using CloudL.Application.Contracts.IServices;
using Microsoft.AspNetCore.Http;

namespace CloudL.AspNetCore.Infrastructure.Services;

/// <summary>
/// 当前用户上下文实现：从 JWT Claims 解析当前请求的用户信息。
/// </summary>
public class CurrentUser : ICurrentUser
{
    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        var principal = httpContextAccessor.HttpContext?.User;
        IsAuthenticated = principal?.Identity?.IsAuthenticated ?? false;

        if (!IsAuthenticated || principal is null)
            return;

        var userIdText = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        UserId = Guid.TryParse(userIdText, out var userId) ? userId : null;
        UserName = principal.FindFirst(ClaimTypes.Name)?.Value;
        UserCode = principal.FindFirst("code")?.Value;
        OrganizationCode = principal.FindFirst("organization_code")?.Value;
        Roles = principal.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public bool IsAuthenticated { get; }

    /// <inheritdoc />
    public Guid? UserId { get; }

    /// <inheritdoc />
    public string? UserCode { get; }

    /// <inheritdoc />
    public string? UserName { get; }

    /// <inheritdoc />
    public string? OrganizationCode { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> Roles { get; } = [];

    /// <inheritdoc />
    public bool HasRole(string roleCode) =>
        !string.IsNullOrEmpty(roleCode) && Roles.Contains(roleCode, StringComparer.Ordinal);

    /// <inheritdoc />
    public bool HasAnyRole(params string[] roleCodes) =>
        roleCodes is { Length: > 0 } && roleCodes.Any(HasRole);
}
