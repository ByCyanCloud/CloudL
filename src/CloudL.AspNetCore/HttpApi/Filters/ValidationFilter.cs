using System.Text.Json;
using CloudL.AspNetCore.HttpApi.Extensions;
using CloudL.AspNetCore.Logging;
using CloudL.Domain.Shared.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace CloudL.AspNetCore.HttpApi.Filters;

/// <summary>
/// 模型验证过滤器：统一校验 ModelState，错误键名转为 snake_case，返回统一 <see cref="ApiResponse{T}"/>。
/// 记录请求体前会做脱敏，避免口令等敏感信息落入日志。
/// </summary>
public class ValidationFilter : IAsyncActionFilter
{
    private readonly ILogger<ValidationFilter> _logger;

    public ValidationFilter(ILogger<ValidationFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.ModelState.IsValid)
        {
            await next().ConfigureAwait(false);
            return;
        }

        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => ToSnakeCasePropertyPath(entry.Key),
                entry => entry.Value!.Errors
                    .Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                        ? "参数不合法"
                        : error.ErrorMessage)
                    .ToArray());

        var response = ApiResponse<object>.Fail(
            ErrorCodes.ValidationError,
            "请求参数验证失败",
            errors: errors);

        var requestBody = SensitiveDataRedactor.RedactBody(
            await context.HttpContext.Request.ReadBodyAsync().ConfigureAwait(false));

        _logger.LogWarning(
            "\n请求参数验证失败\n[ErrorId={ErrorId}]\n{Method} {Path}{QueryString}\n[Body]\n{Body}",
            response.ErrorId,
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path,
            context.HttpContext.Request.QueryString,
            requestBody);

        context.Result = new BadRequestObjectResult(response);
    }

    private static string ToSnakeCasePropertyPath(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(JsonNamingPolicy.SnakeCaseLower.ConvertName));
}
