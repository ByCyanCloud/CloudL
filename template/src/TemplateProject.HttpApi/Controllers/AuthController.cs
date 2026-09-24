using CloudL.AspNetCore.HttpApi.Base;
using CloudL.AspNetCore.HttpApi.Extensions;
using CloudL.Application.Contracts.IServices;
using CloudL.Domain.Shared.Constants;
using Microsoft.AspNetCore.RateLimiting;
using TemplateProject.Application.Contracts.Dtos;
using TemplateProject.Application.Contracts.IServices;

namespace TemplateProject.HttpApi.Controllers;

/// <summary>
/// 认证接口：登录、刷新令牌、登出、改密。
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public class AuthController : BaseApiController
{
    private readonly IUserService _userService;
    private readonly ICurrentUser _currentUser;

    public AuthController(IUserService userService, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(userService);
        ArgumentNullException.ThrowIfNull(currentUser);

        _userService = userService;
        _currentUser = currentUser;
    }

    /// <summary>登录。</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<LoginResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<LoginResponseDto>>> Login(
        [FromBody] LoginDto input,
        CancellationToken cancellationToken)
    {
        var result = await _userService.LoginAsync(input, cancellationToken);
        return OkResponse(result, "登录成功");
    }

    /// <summary>刷新令牌（Refresh Token 一次性使用，返回新的令牌对）。</summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<LoginResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<LoginResponseDto>>> RefreshToken(
        [FromBody] RefreshTokenDto input,
        CancellationToken cancellationToken)
    {
        var result = await _userService.RefreshTokenAsync(input.RefreshToken, cancellationToken);
        return OkResponse(result);
    }

    /// <summary>登出（撤销当前刷新令牌）。</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshTokenDto input,
        CancellationToken cancellationToken)
    {
        await _userService.LogoutAsync(input.RefreshToken, cancellationToken);
        return NoContentResponse();
    }

    /// <summary>修改当前用户密码（成功后该用户全部刷新令牌失效）。</summary>
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordDto input,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return StatusCode(
                StatusCodes.Status401Unauthorized,
                ApiResponse.Fail(ErrorCodes.Unauthorized, "未认证或凭据已失效"));
        }

        await _userService.ChangePasswordAsync(userId, input, cancellationToken);
        return NoContentResponse();
    }
}
