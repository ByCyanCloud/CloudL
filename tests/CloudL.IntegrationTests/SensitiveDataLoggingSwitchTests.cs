using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CloudL.IntegrationTests;

/// <summary>
/// 受测契约：<c>Logging:RevealSensitiveDataInLogs</c> 决定块状日志是<strong>明文显示敏感信息</strong>（默认）
/// 还是<strong>走脱敏</strong>，并且这个决定同时作用于 <c>ValidationFilter</c>（验证失败）与
/// <c>ExceptionHandlingMiddleware</c>（业务异常）两条路径；<c>true</c> 时启动阶段还会有一条且仅一条 <c>⚠️</c> 警告。
/// </summary>
/// <remarks>
/// 走真实 MVC 管道（TestServer）+ 捕获型 logger：断言的是"真正写进日志的消息"，
/// 而不是某个辅助方法的返回值 —— 这样"配置没接到调用点"这类问题也能被拦住。
/// </remarks>
public class SensitiveDataLoggingSwitchTests
{
    private const string LogTitle = "请求参数验证失败";

    private const string BusinessExceptionLogTitle = "业务异常";

    /// <summary>JWT 形状的令牌（脱敏模式按"值的形态"命中，明文模式应原样出现）。</summary>
    private const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.UH_mqU";

    /// <summary>请求体里的敏感值（按键名命中）。</summary>
    private const string SecretValue = "super-secret-value";

    private const string RequestBody = "{\"secret_key\":\"super-secret-value\"}";

    /// <summary>启动横幅的判别串（同时钉住"醒目"所需的字样）。</summary>
    private const string BannerMarker = "日志明文模式已开启";

    /// <summary>配置键（故意写字面量：断言的是配置路径本身，改键名必须让测试失败）。</summary>
    private const string ConfigKey = "Logging:RevealSensitiveDataInLogs";

    /// <summary>
    /// <strong>不配置该键</strong>时也走明文（默认值 <c>true</c>）—— 用真实管道证明默认值确实生效。
    /// </summary>
    [Fact]
    public async Task KeyNotConfigured_ShouldLogPlaintextByDefault()
    {
        var capture = new CapturingLoggerProvider();
        await using var host = await CloudLTestHost.StartAsync(loggerProvider: capture);

        var log = await SendSensitiveRequestAsync(host, capture);

        Assert.Contains("[Auth]\nBearer " + Token, log, StringComparison.Ordinal);
        Assert.Contains(SecretValue, log, StringComparison.Ordinal);
        Assert.DoesNotContain("***", log, StringComparison.Ordinal);
    }

    /// <summary>显式 <c>true</c>（模板里就是显式写的 true）：<c>[Auth]</c> 与 <c>[Body]</c> 都是原文。</summary>
    [Fact]
    public async Task RevealTrue_ShouldLogPlaintextAuthAndBody()
    {
        var capture = new CapturingLoggerProvider();
        await using var host = await CloudLTestHost.StartAsync(
            new Dictionary<string, string?> { [ConfigKey] = "true" },
            capture);

        var log = await SendSensitiveRequestAsync(host, capture);

        Assert.Contains("[Auth]\nBearer " + Token, log, StringComparison.Ordinal);
        Assert.Contains("\"secret_key\":\"" + SecretValue + "\"", log, StringComparison.Ordinal);
        Assert.DoesNotContain("***", log, StringComparison.Ordinal);

        // 明文模式不得破坏块状日志的形状：仍不以换行开头/结尾，空行由输出模板产生
        Assert.False(log.StartsWith('\n') || log.EndsWith('\n'), "消息不得以换行开头/结尾：" + log);
        Assert.DoesNotContain("\n\n", log, StringComparison.Ordinal);
    }

    /// <summary>显式 <c>false</c>：恢复既有脱敏（<c>Bearer ***XXXXXX</c> + 敏感键 <c>***</c>）。</summary>
    [Fact]
    public async Task RevealFalse_ShouldRestoreRedaction()
    {
        var capture = new CapturingLoggerProvider();
        await using var host = await CloudLTestHost.StartAsync(
            new Dictionary<string, string?> { [ConfigKey] = "false" },
            capture);

        var log = await SendSensitiveRequestAsync(host, capture);

        Assert.Contains("[Auth]\nBearer ***UH_mqU", log, StringComparison.Ordinal);
        Assert.Contains("\"secret_key\":\"***\"", log, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretValue, log, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, log, StringComparison.Ordinal);
    }

    /// <summary>空请求体在<strong>两种模式</strong>下都渲染为 <c>(none)</c>（不是敏感信息，不受开关影响）。</summary>
    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public async Task EmptyBody_ShouldRenderNonePlaceholderInBothModes(string reveal)
    {
        var capture = new CapturingLoggerProvider();
        await using var host = await CloudLTestHost.StartAsync(
            new Dictionary<string, string?> { [ConfigKey] = reveal },
            capture);

        var response = await host.Client.GetAsync("/api/validated-query?pageIndex=0&pageSize=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var log = Assert.Single(CaptureValidationFailures(capture)).Message;

        Assert.EndsWith("[Body]\n(none)", log, StringComparison.Ordinal);
        Assert.False(log.EndsWith('\n'), "消息不得以换行结尾：" + log);
    }

    /// <summary>
    /// 业务异常路径（<c>ExceptionHandlingMiddleware</c>）同样受开关控制
    /// —— 否则"关掉开关"只关掉一半，异常日志里照样有明文令牌。
    /// </summary>
    [Fact]
    public async Task RevealTrue_ShouldAlsoApplyToBusinessExceptionPath()
    {
        var capture = new CapturingLoggerProvider();
        await using var host = await CloudLTestHost.StartAsync(
            new Dictionary<string, string?> { [ConfigKey] = "true" },
            capture);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/diagnostic/missing");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var log = Assert.Single(CaptureBusinessExceptions(capture)).Message;

        Assert.Contains("[Auth]\nBearer " + Token, log, StringComparison.Ordinal);
        Assert.DoesNotContain("***", log, StringComparison.Ordinal);
    }

    /// <summary><c>true</c> 时启动横幅恰好一条 —— 连发多个请求也不会变成"每请求一条"。</summary>
    [Fact]
    public async Task StartupBanner_ShouldAppearExactlyOnce_WhenRevealTrue()
    {
        var capture = new CapturingLoggerProvider();
        await using var host = await CloudLTestHost.StartAsync(
            new Dictionary<string, string?> { [ConfigKey] = "true" },
            capture);

        // 多打几个请求：横幅若写在中间件/过滤器里，这里就会看到多条
        for (var index = 0; index < 3; index++)
        {
            await host.Client.GetAsync("/api/validated-query?pageIndex=0&pageSize=0");
            await host.Client.GetAsync("/api/diagnostic/missing");
        }

        var banners = capture.Entries
            .Where(entry => entry.Message.Contains(BannerMarker, StringComparison.Ordinal))
            .ToArray();

        var banner = Assert.Single(banners);

        Assert.Equal(LogLevel.Warning, banner.Level);
        Assert.Contains("⚠️", banner.Message, StringComparison.Ordinal);
        Assert.Contains("明文", banner.Message, StringComparison.Ordinal);
        Assert.Contains("生产环境请改为 false", banner.Message, StringComparison.Ordinal);
        Assert.Contains("Logging:RevealSensitiveDataInLogs", banner.Message, StringComparison.Ordinal);
    }

    /// <summary><c>false</c> 时完全不输出横幅。</summary>
    [Fact]
    public async Task StartupBanner_ShouldNotAppear_WhenRevealFalse()
    {
        var capture = new CapturingLoggerProvider();
        await using var host = await CloudLTestHost.StartAsync(
            new Dictionary<string, string?> { [ConfigKey] = "false" },
            capture);

        await host.Client.GetAsync("/api/validated-query?pageIndex=0&pageSize=0");

        Assert.DoesNotContain(
            capture.Entries,
            entry => entry.Message.Contains(BannerMarker, StringComparison.Ordinal));
    }

    /// <summary>发一次"带令牌头 + 带敏感键请求体"的验证失败请求，返回其块状日志。</summary>
    private static async Task<string> SendSensitiveRequestAsync(CloudLTestHost host, CapturingLoggerProvider capture)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/validated-query/body?pageIndex=0&pageSize=0")
        {
            Content = new StringContent(RequestBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        return Assert.Single(CaptureValidationFailures(capture)).Message;
    }

    private static CapturedLog[] CaptureValidationFailures(CapturingLoggerProvider capture) =>
        capture.Entries
            .Where(entry => entry.Message.Contains(LogTitle, StringComparison.Ordinal))
            .ToArray();

    private static CapturedLog[] CaptureBusinessExceptions(CapturingLoggerProvider capture) =>
        capture.Entries
            .Where(entry => entry.Message.StartsWith(BusinessExceptionLogTitle, StringComparison.Ordinal))
            .ToArray();

    /// <summary>一条被捕获的日志：级别 + 渲染后的消息。</summary>
    internal sealed record CapturedLog(LogLevel Level, string Message);

    /// <summary>把所有日志消息与级别收集起来，供断言使用。</summary>
    internal sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<CapturedLog> _entries = new();

        public IEnumerable<CapturedLog> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly ConcurrentQueue<CapturedLog> _entries;

            public CapturingLogger(ConcurrentQueue<CapturedLog> entries) => _entries = entries;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (formatter is null)
                    return;

                _entries.Enqueue(new CapturedLog(logLevel, formatter(state, exception)));
            }
        }
    }
}
