using System.Net;
using CloudL.AspNetCore.HttpApi.Extensions;
using CloudL.AspNetCore.Json;
using CloudL.AspNetCore.Logging;
using CloudL.Domain.Shared.Constants;
using CloudL.Domain.Shared.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloudL.AspNetCore.HttpApi.Middlewares;

/// <summary>
/// 全局异常处理中间件：把异常转换为统一的 <see cref="ApiResponse{T}"/>。
/// <para><strong>安全约定</strong>：只有框架定义的业务异常才把消息透传给调用方；
/// 其余异常（含 BCL 的 <c>ArgumentException</c>/<c>InvalidOperationException</c>/<c>KeyNotFoundException</c>）
/// 一律返回通用消息 —— 它们几乎都是代码缺陷而不是调用方输入问题。</para>
/// <para>请求体与 Authorization 头在写日志前一律脱敏；堆栈仅在开发环境、且仅对 5xx 返回。</para>
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(environment);

        _next = next;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>处理请求。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 客户端主动断开：不是服务端错误，也不应再尝试写响应体
            _logger.LogInformation(
                "请求被客户端取消: {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception).ConfigureAwait(false);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var errorId = Guid.NewGuid().ToString("N");
        var (statusCode, businessCode, message) = MapException(exception);

        var errorDetail = _environment.IsDevelopment() && statusCode >= HttpStatusCode.InternalServerError
            ? exception.ToString()
            : null;

        var response = ApiResponse<object>.Fail(businessCode, message, errorDetail, errorId: errorId);

        if (context.Response.HasStarted)
        {
            _logger.LogError(
                exception,
                "\n响应已开始，无法写出统一错误响应\n[ErrorId={ErrorId}] {Method} {Path}",
                errorId, context.Request.Method, context.Request.Path);
            return;
        }

        var requestBody = SensitiveDataRedactor.RedactBody(
            await context.Request.ReadBodyAsync().ConfigureAwait(false));

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(CloudLJson.Serialize(response)).ConfigureAwait(false);

        WriteLog(context, exception, response, requestBody, statusCode);
    }

    private void WriteLog(
        HttpContext context,
        Exception exception,
        ApiResponse<object> response,
        string requestBody,
        HttpStatusCode statusCode)
    {
        var authHeader = SensitiveDataRedactor.RedactAuthorizationHeader(
            context.Request.Headers.Authorization.ToString());

        // 日志级别直接由映射出的状态码决定：5xx 是服务端问题（Error），4xx 是预期内的业务问题（Warning）。
        // 用状态码判断，而不是再维护一份"预期异常清单"，避免两处规则随时间不一致。
        if (statusCode >= HttpStatusCode.InternalServerError)
        {
            _logger.LogError(
                exception,
                "\n未处理的异常\n[Response]\nstatusCode={StatusCode}, errorId={ErrorId}, message={Message}\n[Request]\n{Method} {Path}{QueryString}\n[Auth]\n{AuthHeader}\n[Body]\n{Body}",
                response.StatusCode, response.ErrorId, response.Message,
                context.Request.Method, context.Request.Path, context.Request.QueryString,
                authHeader, requestBody);
            return;
        }

        _logger.LogWarning(
            "\n业务异常\n[Response]\nstatusCode={StatusCode}, errorId={ErrorId}, message={Message}\n[Request]\n{Method} {Path}{QueryString}\n[Auth]\n{AuthHeader}\n[Body]\n{Body}",
            response.StatusCode, response.ErrorId, response.Message,
            context.Request.Method, context.Request.Path, context.Request.QueryString,
            authHeader, requestBody);
    }

    private static (HttpStatusCode StatusCode, int BusinessCode, string Message) MapException(Exception exception) =>
        exception switch
        {
            // 注意：子类必须排在父类之前（Unauthorized / Forbidden / NotFound 都继承 BusinessException）
            UnauthorizedBusinessException unauthorized => (HttpStatusCode.Unauthorized, unauthorized.BusinessCode, unauthorized.Message),
            ForbiddenBusinessException forbidden => (HttpStatusCode.Forbidden, forbidden.BusinessCode, forbidden.Message),
            NotFoundException notFound => (HttpStatusCode.NotFound, notFound.BusinessCode, notFound.Message),
            BusinessConflictException conflict => (HttpStatusCode.Conflict, ErrorCodes.Conflict, conflict.Message),
            ConcurrencyConflictException concurrency => (HttpStatusCode.Conflict, ErrorCodes.Conflict, concurrency.Message),

            // 注意：DownstreamTimeoutException 继承 DownstreamServiceException，必须排在前面
            DownstreamTimeoutException timeout => (HttpStatusCode.GatewayTimeout, timeout.BusinessCode, timeout.Message),
            DownstreamServiceException downstream => (HttpStatusCode.BadGateway, downstream.BusinessCode, downstream.Message),

            BusinessException business => (HttpStatusCode.BadRequest, business.BusinessCode, business.Message),

            // 其余一切（含 BCL 异常）都算服务端错误：不透传消息，避免"把代码缺陷报成调用方错误"并泄露实现细节。
            _ => (HttpStatusCode.InternalServerError, ErrorCodes.ServerError, "服务器内部错误")
        };
}
