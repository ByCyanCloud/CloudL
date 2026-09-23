using System.ComponentModel.DataAnnotations;
using TemplateProject.Domain.Shared.Enums;

namespace TemplateProject.Application.Contracts.Dtos;

/// <summary>
/// 用户 DTO。
/// </summary>
public class UserDto
{
    /// <summary>用户 ID。</summary>
    public Guid Id { get; set; }

    /// <summary>用户编码。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>用户名。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>邮箱。</summary>
    public string? Email { get; set; }

    /// <summary>手机号。</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>用户状态。</summary>
    public UserStatus Status { get; set; }

    /// <summary>角色编码列表。</summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>创建时间（UTC）。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>并发令牌，更新时必须回传。</summary>
    public Guid RowVersion { get; set; }
}

/// <summary>
/// 创建用户请求。
/// </summary>
public class CreateUserDto
{
    /// <summary>用户名。</summary>
    [Required(ErrorMessage = "用户名不能为空")]
    [StringLength(64, MinimumLength = 3, ErrorMessage = "用户名长度必须在 3-64 个字符之间")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>密码（明文，仅用于本次请求）。</summary>
    [Required(ErrorMessage = "密码不能为空")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "密码长度必须在 8-128 个字符之间")]
    public string Password { get; set; } = string.Empty;

    /// <summary>邮箱。</summary>
    [EmailAddress(ErrorMessage = "邮箱格式不正确")]
    public string? Email { get; set; }

    /// <summary>手机号。</summary>
    [Phone(ErrorMessage = "手机号格式不正确")]
    public string? PhoneNumber { get; set; }

    /// <summary>角色编码列表，例如 ["user"]。</summary>
    public List<string> Roles { get; set; } = [];
}

/// <summary>
/// 更新用户请求（乐观锁）。
/// </summary>
public class UpdateUserDto
{
    /// <summary>并发令牌，取自上一次查询结果。</summary>
    [Required(ErrorMessage = "并发令牌不能为空")]
    public Guid RowVersion { get; set; }

    /// <summary>邮箱。</summary>
    [EmailAddress(ErrorMessage = "邮箱格式不正确")]
    public string? Email { get; set; }

    /// <summary>手机号。</summary>
    [Phone(ErrorMessage = "手机号格式不正确")]
    public string? PhoneNumber { get; set; }
}

/// <summary>
/// 修改密码请求。
/// </summary>
public class ChangePasswordDto
{
    /// <summary>当前密码。</summary>
    [Required(ErrorMessage = "当前密码不能为空")]
    public string CurrentPassword { get; set; } = string.Empty;

    /// <summary>新密码。</summary>
    [Required(ErrorMessage = "新密码不能为空")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "新密码长度必须在 8-128 个字符之间")]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// 登录请求。
/// </summary>
public class LoginDto
{
    /// <summary>用户名。</summary>
    [Required(ErrorMessage = "用户名不能为空")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>密码。</summary>
    [Required(ErrorMessage = "密码不能为空")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// 登录响应。
/// </summary>
public class LoginResponseDto
{
    /// <summary>访问令牌。</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>刷新令牌。</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>访问令牌过期时间。</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>当前用户。</summary>
    public UserDto User { get; set; } = null!;
}

/// <summary>
/// 刷新令牌请求。
/// </summary>
public class RefreshTokenDto
{
    /// <summary>刷新令牌。</summary>
    [Required(ErrorMessage = "Refresh Token 不能为空")]
    public string RefreshToken { get; set; } = string.Empty;
}
