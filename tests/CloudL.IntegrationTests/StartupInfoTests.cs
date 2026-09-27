using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CloudL.AspNetCore.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// 启动信息：框架应在服务开始监听之后，输出<strong>运行环境</strong>与<strong>监听地址</strong>。
/// </summary>
/// <remarks>
/// 刻意起一个<strong>真实宿主</strong>（而不是断言日志文件）：日志重定向不可靠，
/// 而捕获 logger 是确定性的，也能长期回归。
/// </remarks>
public class StartupInfoTests
{
    [Fact]
    public async Task Startup_ShouldLogEnvironmentAndListeningAddresses()
    {
        var capture = new CapturingLoggerProvider();

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = new string('x', 48),
            ["Jwt:Issuer"] = "startup-info-test",
            ["Jwt:Audience"] = "startup-info-test"
        });
        builder.Logging.AddProvider(capture);
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Services.AddCloudLAspNetCore(builder.Configuration);
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        await using var app = builder.Build();

        await app.StartAsync();
        await Task.Delay(500);
        await app.StopAsync();

        var messages = capture.Messages.ToArray();

        Assert.True(
            messages.Any(message =>
                message.Contains("运行环境", StringComparison.Ordinal) &&
                message.Contains("监听地址", StringComparison.Ordinal)),
            "启动日志里没有运行环境与监听地址。实际捕获到的日志：" + string.Join(" | ", messages));
    }

    /// <summary>把所有日志消息收集起来，供断言使用。</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IEnumerable<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly ConcurrentQueue<string> _messages;

            public CapturingLogger(ConcurrentQueue<string> messages) => _messages = messages;

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

                _messages.Enqueue(formatter(state, exception));
            }
        }
    }
}
