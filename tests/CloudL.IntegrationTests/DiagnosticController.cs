using CloudL.AspNetCore.Extensions;
using CloudL.AspNetCore.HttpApi.Base;
using CloudL.AspNetCore.HttpApi.Extensions;
using CloudL.Application.Contracts.IServices;
using CloudL.Domain.Shared.Constants;
using CloudL.Domain.Shared.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CloudL.IntegrationTests;

/// <summary>集成测试用的最小控制器：只暴露验证框架管道行为所需的端点。</summary>
[ApiController]
[Route("api/diagnostic")]
[AllowAnonymous]
public class DiagnosticController : BaseApiController
{
    private readonly ILoginAttemptGuard _loginAttemptGuard;

    public DiagnosticController(ILoginAttemptGuard loginAttemptGuard)
    {
        ArgumentNullException.ThrowIfNull(loginAttemptGuard);
        _loginAttemptGuard = loginAttemptGuard;
    }

    [HttpGet("echo")]
    public ActionResult<ApiResponse<EchoResult>> Echo(
        [FromQuery] int page_index,
        [FromQuery] int page_size)
        => OkResponse(new EchoResult(page_index, page_size));

    /// <summary>抛出 BCL 异常：应映射为 500（而不是 400），且不透传消息。</summary>
    [HttpGet("boom")]
    public ActionResult<ApiResponse<string>> Boom() =>
        throw new InvalidOperationException("集成测试故意抛出的异常");

    /// <summary>抛出框架业务异常：应映射为 404 并透传消息。</summary>
    [HttpGet("missing")]
    public ActionResult<ApiResponse<string>> Missing() =>
        throw new NotFoundException("集成测试：资源不存在");

    /// <summary>抛出框架业务异常：应映射为 403。</summary>
    [HttpGet("denied")]
    public ActionResult<ApiResponse<string>> Denied() =>
        throw new ForbiddenBusinessException("集成测试：权限不足");

    /// <summary>模拟一次登录失败：先检查锁定，再计入失败。</summary>
    [HttpPost("login-failure")]
    public ActionResult<ApiResponse<string>> LoginFailure([FromQuery] string user_name)
    {
        _loginAttemptGuard.EnsureNotLocked(user_name);
        _loginAttemptGuard.RecordFailure(user_name);

        throw new UnauthorizedBusinessException(ErrorCodes.CredentialsError, "用户名或密码错误");
    }

    /// <summary>模拟一次登录成功：应清除失败计数。</summary>
    [HttpPost("login-success")]
    public ActionResult<ApiResponse<string>> LoginSuccess([FromQuery] string user_name)
    {
        _loginAttemptGuard.EnsureNotLocked(user_name);
        _loginAttemptGuard.Reset(user_name);

        return OkResponse("ok");
    }

    [HttpGet("limited")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public ActionResult<ApiResponse<string>> Limited() => OkResponse("ok");

    /// <summary>回显结果（属性名本身就是 snake_case，避免与命名约定冲突）。</summary>
    public sealed record EchoResult(int page_index, int page_size);
}
