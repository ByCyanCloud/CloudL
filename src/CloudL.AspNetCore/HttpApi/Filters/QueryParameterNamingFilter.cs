using CloudL.AspNetCore.HttpApi.Binding;
using CloudL.AspNetCore.HttpApi.Extensions;
using CloudL.Domain.Shared.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace CloudL.AspNetCore.HttpApi.Filters;

/// <summary>
/// query 参数命名校验：只接受 snake_case（单词键如 <c>id</c> 同样合法），
/// 出现 camelCase / PascalCase（即含大写字母）时返回 <strong>HTTP 400</strong>。
/// </summary>
/// <remarks>
/// 为什么必须"报错"而不是"忽略"：参数名不匹配时模型绑定会静默使用默认值，
/// 调用方拿到的是错误结果却没有任何提示 —— 这是最难排查的一类问题。
/// </remarks>
public sealed class QueryParameterNamingFilter : IActionFilter
{
    private readonly ILogger<QueryParameterNamingFilter> _logger;

    public QueryParameterNamingFilter(ILogger<QueryParameterNamingFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.HttpContext.Request;
        if (request.Query.Count == 0)
            return;

        var invalidKeys = request.Query.Keys
            .Where(key => !SnakeCaseQueryKey.IsSnakeCase(key))
            .ToArray();

        if (invalidKeys.Length == 0)
            return;

        _logger.LogWarning(
            "\nquery 参数命名不符合约定\n[Request]\n{Method} {Path}{QueryString}\n[InvalidKeys]\n{InvalidKeys}",
            request.Method,
            request.Path,
            request.QueryString,
            string.Join(", ", invalidKeys));

        var errors = invalidKeys.ToDictionary(
            key => key,
            _ => new[] { "参数名必须为 snake_case：仅小写字母、数字、下划线，嵌套用点分隔" });

        context.Result = new BadRequestObjectResult(
            ApiResponse<object>.Fail(
                ErrorCodes.ValidationError,
                $"query 参数名必须使用 snake_case，以下参数名不合法: {string.Join(", ", invalidKeys)}",
                errors: errors));
    }

    /// <inheritdoc />
    public void OnActionExecuted(ActionExecutedContext context)
    {
        // 无需处理
    }
}
