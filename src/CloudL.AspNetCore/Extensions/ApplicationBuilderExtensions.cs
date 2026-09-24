using CloudL.AspNetCore.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Events;

namespace CloudL.AspNetCore.Extensions;

/// <summary>
/// 应用管道（<see cref="IApplicationBuilder"/>）扩展。
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>这些路径的请求日志降级为 Verbose，避免健康检查与接口文档淹没业务日志。</summary>
    private static readonly string[] NoisePaths = ["/health", "/swagger", "/favicon.ico"];

    /// <summary>
    /// 启用请求日志：每个请求输出一条结构化摘要
    /// （方法、路径、脱敏后的 query、状态码、耗时、TraceId、客户端 IP、用户名）。
    /// </summary>
    /// <remarks>
    /// <para>请在 <c>ExceptionHandlingMiddleware</c> <strong>之前</strong>调用：异常中间件会写出统一错误响应、
    /// 状态码此时已定型，请求日志因此能记录到最终状态码；同时避免与异常中间件的详细异常日志重复。</para>
    /// <para>query string 中的敏感参数（如 <c>access_token</c>）会被自动遮蔽 —— 否则令牌会随日志落盘。</para>
    /// </remarks>
    public static IApplicationBuilder UseCloudLRequestLogging(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "{RequestMethod} {RequestPath}{RedactedQuery} -> {StatusCode} in {Elapsed:0.0} ms";

            options.GetLevel = ResolveLevel;

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
                diagnosticContext.Set(
                    "RedactedQuery",
                    SensitiveDataRedactor.RedactQueryString(httpContext.Request.QueryString.Value));

                var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
                if (!string.IsNullOrEmpty(clientIp))
                    diagnosticContext.Set("ClientIp", clientIp);

                var userName = httpContext.User?.Identity?.Name;
                if (!string.IsNullOrEmpty(userName))
                    diagnosticContext.Set("UserName", userName);
            };
        });
    }

    private static LogEventLevel ResolveLevel(
        HttpContext httpContext,
        double elapsedMilliseconds,
        Exception? exception)
    {
        if (exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            return LogEventLevel.Error;

        if (httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest)
            return LogEventLevel.Warning;

        foreach (var noisePath in NoisePaths)
        {
            if (httpContext.Request.Path.StartsWithSegments(noisePath, StringComparison.OrdinalIgnoreCase))
                return LogEventLevel.Verbose;
        }

        return LogEventLevel.Information;
    }
}
