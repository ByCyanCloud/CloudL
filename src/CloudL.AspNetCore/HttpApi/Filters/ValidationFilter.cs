using System.Text;
using System.Text.Json;
using CloudL.AspNetCore.HttpApi.Extensions;
using CloudL.AspNetCore.Logging;
using CloudL.Domain.Shared.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;

namespace CloudL.AspNetCore.HttpApi.Filters;

/// <summary>
/// 模型验证过滤器：统一校验 ModelState，错误键名转为 snake_case，返回统一 <see cref="ApiResponse{T}"/>。
/// 记录请求体前会做脱敏，避免口令等敏感信息落入日志。
/// </summary>
public class ValidationFilter : IAsyncActionFilter
{
    /// <summary>日志里最多列出的失败字段数：只用于日志，响应里的错误项一个不少。</summary>
    private const int MaxLoggedErrorFields = 30;

    /// <summary>日志里失败字段明细的长度上限（字符）：超出即截断并标注，避免单条日志被灌爆。</summary>
    private const int MaxLoggedErrorsLength = 1000;

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

        // 注意：消息字符串内不得以换行开头/结尾 —— 块状日志的空行由 Serilog 输出模板最前面的 {NewLine} 产生。
        _logger.LogWarning(
            "请求参数验证失败\n[ErrorId={ErrorId}]\n{Method} {Path}{QueryString}\n[Errors]\n{Errors}\n[Body]\n{Body}",
            response.ErrorId,
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path,
            context.HttpContext.Request.QueryString,
            FormatErrorsForLog(context.ModelState),
            requestBody);

        context.Result = new BadRequestObjectResult(response);
    }

    /// <summary>
    /// 把失败字段拼成紧凑的一行供日志排查使用：<c>page_index: 页码必须大于等于 1; sort_by: 排序字段不支持</c>。
    /// 字段名与响应里的错误键一致（snake_case）；同一字段只取<strong>第一条</strong>错误。
    /// 最多列 <see cref="MaxLoggedErrorFields"/> 个字段，整体不超过 <see cref="MaxLoggedErrorsLength"/> 字符。
    /// </summary>
    /// <remarks>
    /// <c>internal</c> 仅为让测试能直接喂 <see cref="ModelStateDictionary"/> 覆盖"超上限截断"这类边界
    /// （见 <c>CloudL.IntegrationTests</c>）；它<strong>不是</strong>对外契约，随时可改。
    /// </remarks>
    internal static string FormatErrorsForLog(ModelStateDictionary modelState)
    {
        var failed = modelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToArray();

        var builder = new StringBuilder();

        for (var index = 0; index < failed.Length && index < MaxLoggedErrorFields; index++)
        {
            if (index > 0)
            {
                builder.Append("; ");
            }

            builder
                .Append(ToSnakeCasePropertyPath(failed[index].Key))
                .Append(": ")
                .Append(PickErrorMessage(failed[index].Value!.Errors[0]));
        }

        if (failed.Length > MaxLoggedErrorFields)
        {
            builder.Append("…（共 ").Append(failed.Length).Append(" 项）");
        }

        if (builder.Length > MaxLoggedErrorsLength)
        {
            var dropped = builder.Length - MaxLoggedErrorsLength;
            builder.Length = MaxLoggedErrorsLength;
            builder.Append("…（已截断 ").Append(dropped).Append(" 字符）");
        }

        return builder.ToString();
    }

    /// <summary>取第一条错误的消息：优先 <see cref="ModelError.ErrorMessage"/>，为空时退回异常消息。</summary>
    private static string PickErrorMessage(ModelError error)
    {
        if (!string.IsNullOrEmpty(error.ErrorMessage))
        {
            return Flatten(error.ErrorMessage);
        }

        if (!string.IsNullOrEmpty(error.Exception?.Message))
        {
            return Flatten(error.Exception!.Message);
        }

        // 与响应侧的兜底文案保持一致。
        return "参数不合法";
    }

    /// <summary>把消息里的换行/制表符压成空格，保证 [Errors] 段始终是紧凑的一行。</summary>
    private static string Flatten(string message) =>
        message.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();

    private static string ToSnakeCasePropertyPath(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(JsonNamingPolicy.SnakeCaseLower.ConvertName));
}
