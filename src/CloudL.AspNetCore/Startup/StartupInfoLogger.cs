using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudL.AspNetCore.Configuration;
using CloudL.AspNetCore.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloudL.AspNetCore.Startup;

/// <summary>
/// 启动时输出<strong>运行环境</strong>与<strong>监听地址</strong>，
/// 并在"日志明文显示敏感信息"开启时输出一条<strong>醒目的一次性警告</strong>。
/// </summary>
/// <remarks>
/// <para>由 <c>AddCloudLAspNetCore</c> 自动注册，业务项目无需任何配置。</para>
/// <para>日志写在 <see cref="IHostApplicationLifetime.ApplicationStarted"/> 回调里，而不是
/// <see cref="StartAsync"/> 里 —— 因为托管服务启动时服务器<strong>还没有真正绑定端口</strong>，
/// 那时读地址会拿到空值；<c>ApplicationStarted</c> 是服务器已开始监听之后才触发的。</para>
/// <para>明文警告也因此<strong>只输出一次</strong>（<c>ApplicationStarted</c> 每进程只触发一次），
/// 而不是每个请求刷一次屏。</para>
/// </remarks>
internal sealed class StartupInfoLogger : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServer _server;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<StartupInfoLogger> _logger;
    private readonly ISensitiveDataLogPolicy _sensitiveDataLogPolicy;

    public StartupInfoLogger(
        IHostApplicationLifetime lifetime,
        IServer server,
        IHostEnvironment environment,
        ILogger<StartupInfoLogger> logger,
        ISensitiveDataLogPolicy sensitiveDataLogPolicy)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(sensitiveDataLogPolicy);

        _lifetime = lifetime;
        _server = server;
        _environment = environment;
        _logger = logger;
        _sensitiveDataLogPolicy = sensitiveDataLogPolicy;
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

        WriteSensitiveDataRevealWarning();
    }

    /// <summary>
    /// 明文日志的一次性启动警告：只在这里（<c>ApplicationStarted</c>，每进程一次）输出，
    /// 不放在中间件/过滤器里 —— 那样会变成每请求刷屏。
    /// </summary>
    private void WriteSensitiveDataRevealWarning()
    {
        if (!_sensitiveDataLogPolicy.RevealSensitiveData)
            return;

        _logger.LogWarning(
            "⚠️ 日志明文模式已开启（{Section}:{Key}=true，这是默认值）："
            + "[Auth] 请求头、[Body] 请求体与 query 中的敏感值会按【明文】写入日志，"
            + "口令/令牌/密钥会随日志落盘。生产环境请改为 false。",
            SensitiveDataLoggingOptions.SectionName,
            SensitiveDataLoggingOptions.RevealSensitiveDataInLogsKey);
    }
}
