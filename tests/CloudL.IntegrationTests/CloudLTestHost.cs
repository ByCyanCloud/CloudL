using System.Net;
using CloudL.AspNetCore.Extensions;
using CloudL.AspNetCore.HttpApi.Filters;
using CloudL.AspNetCore.HttpApi.Middlewares;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CloudL.IntegrationTests;

/// <summary>
/// 进程内测试宿主：用<strong>框架的全套约定</strong>（统一响应、全局异常、query 命名校验、限流）
/// 启动一个最小 Web API，用来做真实管道的集成测试。
/// </summary>
/// <remarks>
/// 之所以需要它：限流是否真的返回 429、异常是否真的变成统一 500、query 命名是否真的拦截，
/// 都无法用纯单元测试覆盖 —— 这些行为由 MVC 管道与 DI 装配决定，只有在真实管道里跑才作数。
/// </remarks>
internal sealed class CloudLTestHost : IAsyncDisposable
{
    private const string SecretKey = "integration-test-secret-key-must-be-long-enough-to-pass-validation";

    private CloudLTestHost(IHost host, HttpClient client)
    {
        _host = host;
        Client = client;
    }

    private readonly IHost _host;

    /// <summary>用于发起请求的客户端。</summary>
    public HttpClient Client { get; }

    /// <summary>启动测试宿主；<paramref name="overrides"/> 可覆盖任意配置项。</summary>
    public static async Task<CloudLTestHost> StartAsync(IDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Jwt:SecretKey"] = SecretKey,
            ["Jwt:Issuer"] = "CloudL.IntegrationTests",
            ["Jwt:Audience"] = "CloudL.IntegrationTests"
        };

        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                settings[pair.Key] = pair.Value;
            }
        }

        // 测试专用：允许用请求头伪造客户端 IP，用于验证"按 IP 分区"确实生效。
        // 生产环境绝不能这样信任请求头（必须显式配置可信代理 + ForwardedHeaders）。
        var useClientIpHeader = settings.TryGetValue("Testing:UseClientIpHeader", out var flag)
            && string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);

        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment("Production");
                webBuilder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings));

                webBuilder.ConfigureServices((context, services) =>
                {
                    services.AddCloudLAspNetCore(context.Configuration);
                    services.AddAuthorization();

                    services
                        .AddControllers(options => options.Filters.Add<ValidationFilter>())
                        .AddApplicationPart(typeof(DiagnosticController).Assembly)
                        .AddCloudLJsonOptions();

                    services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);
                });

                webBuilder.Configure(app =>
                {
                    if (useClientIpHeader)
                    {
                        app.Use(async (context, next) =>
                        {
                            if (context.Request.Headers.TryGetValue("X-Test-Client-IP", out var value)
                                && IPAddress.TryParse(value.ToString(), out var address))
                            {
                                context.Connection.RemoteIpAddress = address;
                            }

                            await next(context);
                        });
                    }

                    app.UseMiddleware<ExceptionHandlingMiddleware>();
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .StartAsync()
            .ConfigureAwait(false);

        return new CloudLTestHost(host, host.GetTestClient());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync().ConfigureAwait(false);
        _host.Dispose();
    }
}
