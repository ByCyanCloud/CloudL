using CloudL.AspNetCore.HttpApi.Base;
using CloudL.AspNetCore.HttpApi.Extensions;
using CloudL.Application.Contracts.Dtos;
using CloudL.Application.Contracts.IServices;
using TemplateProject.Application.Contracts.Dtos;
using TemplateProject.Application.Contracts.IServices;

namespace TemplateProject.HttpApi.Controllers;

/// <summary>
/// 用户管理接口。
/// </summary>
/// <remarks>
/// 类级别的 <see cref="AuthorizeAttribute"/> 之外，全站还配置了
/// <c>FallbackPolicy</c>（要求已认证），因此<strong>新增接口默认就是受保护的</strong>；
/// 需要匿名访问时必须显式标注 <see cref="AllowAnonymousAttribute"/>。
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize]
public class UsersController : BaseApiController
{
    private readonly IUserService _userService;
    private readonly ICurrentUser _currentUser;

    public UsersController(IUserService userService, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(userService);
        ArgumentNullException.ThrowIfNull(currentUser);

        _userService = userService;
        _currentUser = currentUser;
    }

    /// <summary>按 ID 获取用户。</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var user = await _userService.GetByIdAsync(id, cancellationToken);
        return user is null
            ? NotFoundResponse<UserDto>("用户不存在")
            : OkResponse(user);
    }

    /// <summary>分页获取用户列表。</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponseDto<UserDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponseDto<UserDto>>>> GetPagedList(
        [FromQuery] PagedRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _userService.GetPagedListAsync(request, cancellationToken);
        return OkResponse(result);
    }

    /// <summary>创建用户（需要管理员及以上角色）。</summary>
    [HttpPost]
    [Authorize(Policy = "AdminOrAbove")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Create(
        [FromBody] CreateUserDto input,
        CancellationToken cancellationToken)
    {
        var user = await _userService.CreateAsync(input, cancellationToken);
        return CreatedResponse(user);
    }

    /// <summary>更新用户资料（乐观锁，需回传 row_version）。</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Update(
        Guid id,
        [FromBody] UpdateUserDto input,
        CancellationToken cancellationToken)
    {
        var user = await _userService.UpdateAsync(id, input, cancellationToken);
        return OkResponse(user);
    }

    /// <summary>删除用户（需要管理员及以上角色）。</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOrAbove")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _userService.DeleteAsync(id, cancellationToken);
        return NoContentResponse();
    }

    /// <summary>获取当前登录用户信息。</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetCurrentUser(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return UnauthorizedResponse<UserDto>();

        var user = await _userService.GetByIdAsync(userId, cancellationToken);
        return user is null
            ? NotFoundResponse<UserDto>("用户不存在")
            : OkResponse(user);
    }
}
