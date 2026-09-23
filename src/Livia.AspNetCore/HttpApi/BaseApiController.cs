using Livia.AspNetCore.HttpApi.Extensions;
using Livia.Domain.Shared.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Livia.AspNetCore.HttpApi.Base;

/// <summary>
/// 控制器基类，统一 <see cref="ApiResponse{T}"/> 包装，减少业务控制器里的样板代码。
/// </summary>
[ApiController]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>返回 200 与统一响应体。</summary>
    protected ActionResult<ApiResponse<T>> OkResponse<T>(T data, string? message = null) =>
        Ok(ApiResponse<T>.Ok(data, message));

    /// <summary>返回 201 与统一响应体。</summary>
    protected ActionResult<ApiResponse<T>> CreatedResponse<T>(T data, string? message = null) =>
        StatusCode(StatusCodes.Status201Created, ApiResponse<T>.Created(data, message));

    /// <summary>返回 204。</summary>
    protected new IActionResult NoContentResponse() => NoContent();

    /// <summary>返回指定 HTTP 状态码与统一失败响应体。</summary>
    protected ActionResult<ApiResponse<T>> FailResponse<T>(
        int businessCode,
        string message,
        int httpStatusCode = StatusCodes.Status400BadRequest) =>
        StatusCode(httpStatusCode, ApiResponse<T>.Fail(businessCode, message));

    /// <summary>资源不存在（404）。</summary>
    protected ActionResult<ApiResponse<T>> NotFoundResponse<T>(string? message = null) =>
        StatusCode(StatusCodes.Status404NotFound, ApiResponse<T>.Fail(ErrorCodes.NotFound, message ?? "资源不存在"));

    /// <summary>未认证（401）。</summary>
    protected ActionResult<ApiResponse<T>> UnauthorizedResponse<T>(string? message = null) =>
        StatusCode(StatusCodes.Status401Unauthorized, ApiResponse<T>.Fail(ErrorCodes.Unauthorized, message ?? "未认证或凭据已失效"));

    /// <summary>权限不足（403）。</summary>
    protected ActionResult<ApiResponse<T>> ForbiddenResponse<T>(string? message = null) =>
        StatusCode(StatusCodes.Status403Forbidden, ApiResponse<T>.Fail(ErrorCodes.Forbidden, message ?? "权限不足"));
}