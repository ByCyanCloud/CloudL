using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using CloudL.AspNetCore.HttpApi.Extensions;
using CloudL.AspNetCore.Json;
using CloudL.Domain.Shared.Constants;
using Microsoft.AspNetCore.Builder; // AddRateLimiter 定义在 Microsoft.AspNetCore.Builder 命名空间下
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudL.AspNetCore.Extensions;

/// <summary>框架内置的限流策略名。</summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// 认证类接口（登录 / 刷新令牌 / 登出 / 改密）：按客户端 IP 分区限流。
    /// <para>按用户名维度的防护由 <c>ILoginAttemptGuard</c> 在登录流程内完成 ——
    /// 中间件阶段拿不到用户名（它在请求体里），强行读取请求体既昂贵又不可靠。</para>
    /// </summary>
    public const string Auth = "cloudl-auth";
}

/// <summary>限流配置（配置节 <c>RateLimits</c>）。</summary>
public sealed class RateLimitOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "RateLimits";

    /// <summary>认证类接口的限流参数。</summary>
    public AuthRateLimitOptions Auth { get; set; } = new();
}

/// <summary>认证类接口的限流参数。</summary>
public sealed class AuthRateLimitOptions
{
    /// <summary>窗口内允许的请求数（按客户端 IP）。默认 10 次 / 分钟。</summary>
    [Range(1, 100000)]
    public int PermitLimit { get; set; } = 10;

    /// <summary>窗口长度（秒）。</summary>
    [Range(1, 86400)]
    public int WindowSeconds { get; set; } = 60;

    /// <summary>超出配额后的排队数，0 表示立即拒绝。</summary>
    [Range(0, 10000)]
    public int QueueLimit { get; set; }
}

/// <summary>
/// 限流注册。
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>
    /// 注册框架限流策略。已由 <c>AddCloudLAspNetCore</c> 自动调用；
    /// 单独使用时记得在管道里加上 <c>app.UseRateLimiter()</c>（需在 <c>UseRouting</c> 之后）。
    /// </summary>
    /// <remarks>
    /// <strong>部署前提</strong>：按 IP 分区时，若服务在反向代理 / 网关之后，
    /// 必须先启用 ForwardedHeaders（否则所有请求的 RemoteIpAddress 都是代理地址，
    /// 会导致所有人共用一个配额）。框架不默认开启，因为信任转发头需要显式配置可信代理。
    /// </remarks>
    public static IServiceCollection AddCloudLRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RateLimitPolicies.Auth, httpContext =>
            {
                var authOptions = httpContext.RequestServices
                    .GetRequiredService<IOptions<RateLimitOptions>>()
                    .Value
                    .Auth;

                return RateLimitPartition.GetSlidingWindowLimiter(
                    ResolveClientKey(httpContext),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = authOptions.PermitLimit,
                        Window = TimeSpan.FromSeconds(authOptions.WindowSeconds),
                        SegmentsPerWindow = 6,
                        QueueLimit = authOptions.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            options.OnRejected = WriteRejectionAsync;
        });

        return services;
    }

    /// <summary>按客户端 IP 分区；IPv4-mapped IPv6 会归一化，避免同一客户端占用两个桶。</summary>
    private static string ResolveClientKey(HttpContext httpContext)
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress;

        if (remoteIp is null)
            return "unknown";

        if (remoteIp.IsIPv4MappedToIPv6)
            remoteIp = remoteIp.MapToIPv4();

        return remoteIp.ToString();
    }

    /// <summary>
    /// 被限流时写出<strong>框架统一响应格式</strong>（默认的裸 429 与其它错误不一致，调用方得写两套解析逻辑）。
    /// </summary>
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            httpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
        }

        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("CloudL.AspNetCore.RateLimiting");

        logger.LogWarning(
            "\n请求被限流\n[Request]\n{Method} {Path}\n[ClientIp]\n{ClientIp}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            ResolveClientKey(httpContext));

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.ContentType = "application/json; charset=utf-8";

        var payload = ApiResponse.Fail(ErrorCodes.TooManyRequests, "请求过于频繁，请稍后再试");
        await httpContext.Response.WriteAsync(CloudLJson.Serialize(payload), cancellationToken).ConfigureAwait(false);
    }
}
