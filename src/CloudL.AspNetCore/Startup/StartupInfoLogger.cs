using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloudL.AspNetCore.Startup;

/// <summary>
/// 启动时输出<strong>运行环境</strong>与<strong>监听地址</strong>。
/// </summary>
/// <remarks>
/// <para>由 <c>AddCloudLAspNetCore</c> 自动注册，业务项目无需任何配置。</para>
/// <para>日志写在 <see cref="IHostApplicationLifetime.ApplicationStarted"/> 回调里，而不是
/// <see cref="StartAsync"/> 里 —— 因为托管服务启动时服务器<strong>还没有真正绑定端口</strong>，
/// 那时读地址会拿到空值；<c>ApplicationStarted</c> 是服务器已开始监听之后才触发的。</para>
/// </remarks>
internal sealed class StartupInfoLogger : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServer _server;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<StartupInfoLogger> _logger;

    public StartupInfoLogger(
        IHostApplicationLifetime lifetime,
        IServer server,
        IHostEnvironment environment,
        ILogger<StartupInfoLogger> logger)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        _lifetime = lifetime;
        _server = server;
        _environment = environment;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetime.ApplicationStarted.Register(WriteStartupInfo);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void WriteStartupInfo()
    {
        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;

        var listening = addresses is null || addresses.Count == 0
            ? "(未报告监听地址)"
            : string.Join(", ", addresses.OrderBy(address => address, StringComparer.Ordinal));

        var environment = _environment.EnvironmentName;

        var kind = _environment.IsDevelopment() ? "开发环境"
            : _environment.IsProduction() ? "生产环境"
            : "自定义环境";

        var applicationName = _environment.ApplicationName;

        _logger.LogInformation(
            "服务已启动 —— 应用={ApplicationName}，运行环境={Environment}（{Kind}），监听地址：{Addresses}",
            applicationName,
            environment,
            kind,
            listening);
    }
}
