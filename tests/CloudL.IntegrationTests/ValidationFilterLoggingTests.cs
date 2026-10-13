using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// 受测契约：模型验证失败时，<see cref="CloudL.AspNetCore.HttpApi.Filters.ValidationFilter"/> 的块状日志里
/// 必须带上<strong>失败字段名（snake_case）与消息</strong> —— 否则排查时只能人肉反推请求体。
/// </summary>
/// <remarks>
/// 走真实 MVC 管道（TestServer）+ 捕获型 logger：日志内容由过滤器真正写出的消息决定，
/// 而不是断言某个辅助方法 —— 这样"格式改了但忘了拼进模板"这类问题也能被拦住。
/// </remarks>
public class ValidationFilterLoggingTests
{
    private const string LogTitle = "请求参数验证失败";

    [Fact]
    public async Task InvalidModelState_ShouldLogFailedFieldNamesAndMessages()
    {
        var capture = new CapturingLoggerProvider();
        await using var host = await CloudLTestHost.StartAsync(loggerProvider: capture);

        var response = await host.Client.GetAsync("/api/validated-query?pageIndex=0&pageSize=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var log = Assert.Single(CaptureValidationFailure(capture));

        // 字段名用 snake_case（与响应里的错误键一致），消息取 ModelState 的 ErrorMessage。
        Assert.Contains("[Errors]", log, StringComparison.Ordinal);
        Assert.Contains("page_index: 页码必须大于等于 1", log, StringComparison.Ordinal);
        Assert.Contains("page_size: 每页条数必须大于等于 1", log, StringComparison.Ordinal);

        // 响应结构不变：错误键同样是 snake_case（这条只是防止日志改动误伤响应）。
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.True(body["errors"]!["page_index"] is not null);
    }

    [Fact]
    public async Task ManyFailedFields_ShouldLogAllWhenUnderCap()
    {
        var capture = new CapturingLoggerProvider();
        await using var host = await CloudLTestHost.StartAsync(loggerProvider: capture);

        var response = await host.Client.GetAsync("/api/validated-query/many");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var log = Assert.Single(CaptureValidationFailure(capture));

        // 最多列 30 个字段，超出时标注总项数。
        Assert.Equal(12, CountOccurrences(log, "字段必填"));
        // 上限是 30，12 个字段不会触发截断标注
        Assert.DoesNotContain("…（共", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoreThanThirtyFailedFields_ShouldLogThirtyWithTotalCount()
    {
        var capture = new CapturingLoggerProvider();
        await using var host = await CloudLTestHost.StartAsync(loggerProvider: capture);

        var response = await host.Client.GetAsync("/api/validated-query/overflow");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var log = Assert.Single(CaptureValidationFailure(capture));

        // 失败 40 个字段：日志只列前 30 个，并标注实际项数。
        Assert.Equal(30, CountOccurrences(log, "字段必填"));
        Assert.Contains("…（共 40 项）", log, StringComparison.Ordinal);
        Assert.DoesNotContain("…（已截断", log, StringComparison.Ordinal);

        // 响应结构不变：截断只发生在日志里，errors 里 40 条一个不少。
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(40, body["errors"]!.AsObject().Count);
    }

    [Fact]
    public async Task OversizedErrorDetail_ShouldBeTruncated()
    {
        var capture = new CapturingLoggerProvider();
        await using var host = await CloudLTestHost.StartAsync(loggerProvider: capture);

        var response = await host.Client.GetAsync("/api/validated-query/huge");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var log = Assert.Single(CaptureValidationFailure(capture));

        // 单条消息长度上限 1000 字符：截断并标注，且不能把整条日志撑爆。
        Assert.Contains("…（已截断", log, StringComparison.Ordinal);
        Assert.True(log.Length < 1200, "日志未被限长，实际长度：" + log.Length);
    }

    /// <summary>只挑出验证失败那条日志 —— 宿主/限流等其它日志不参与断言。</summary>
    private static string[] CaptureValidationFailure(CapturingLoggerProvider capture) =>
        capture.Messages
            .Where(message => message.Contains(LogTitle, StringComparison.Ordinal))
            .ToArray();

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
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

/// <summary>两个字段都带约束：验证失败时日志里应同时出现两个字段名。</summary>
public sealed class ValidatedQueryDto
{
    [Range(1, int.MaxValue, ErrorMessage = "页码必须大于等于 1")]
    public int PageIndex { get; set; }

    [Range(1, 100, ErrorMessage = "每页条数必须大于等于 1")]
    public int PageSize { get; set; }
}

/// <summary>12 个必填字段：用于验证"最多列 30 个 + 标注总项数"。</summary>
public sealed class ManyFieldsDto
{
    [Required(ErrorMessage = "字段必填")]
    public string? Field01 { get; set; }

    [Required(ErrorMessage = "字段必填")]
    public string? Field02 { get; set; }

    [Required(ErrorMessage = "字段必填")]
    public string? Field03 { get; set; }

    [Required(ErrorMessage = "字段必填")]
    public string? Field04 { get; set; }

    [Required(ErrorMessage = "字段必填")]
    public string? Field05 { get; set; }

    [Required(ErrorMessage = "字段必填")]
    public string? Field06 { get; set; }

    [Required(ErrorMessage = "字段必填")]
    public string? Field07 { get; set; }

    [Required(ErrorMessage = "字段必填")]
    public string? Field08 { get; set; }

    [Required(ErrorMessage = "字段必填")]
    public string? Field09 { get; set; }

    [Required(ErrorMessage = "字段必填")]
    public string? Field10 { get; set; }

    [Required(ErrorMessage = "字段必填")]
    public string? Field11 { get; set; }

    [Required(ErrorMessage = "字段必填")]
    public string? Field12 { get; set; }
}

/// <summary>
/// 一次产生 40 条字段级错误：用于端到端验证"失败字段超过 30 个时只列前 30 个 + 标注总项数"。
/// </summary>
/// <remarks>
/// 用 <see cref="IValidatableObject"/> 而不是堆 40 个 [Required] 属性：错误经由 MVC 自己的校验管道进 ModelState，
/// 与真实请求同一条路径，但不必维护 40 个属性。
/// </remarks>
public sealed class OverflowFieldsDto : IValidatableObject
{
    /// <summary>失败字段条数，必须大于日志上限（30）。</summary>
    public const int FailedFieldCount = 40;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        for (var index = 1; index <= FailedFieldCount; index++)
        {
            yield return new ValidationResult("字段必填", new[] { $"field_{index:D2}" });
        }
    }
}

/// <summary>单条错误消息超长：用于验证长度上限。</summary>
public sealed class HugeMessageDto
{
    [HugeMessage]
    public string? Anything { get; set; }
}

/// <summary>故意返回一条 1500 字符的错误消息。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HugeMessageAttribute : ValidationAttribute
{
    protected override ValidationResult IsValid(object? value, ValidationContext validationContext) =>
        new(new string('长', 1500));
}

[ApiController]
[Route("api/validated-query")]
public sealed class ValidatedQueryController : ControllerBase
{
    [HttpGet]
    public ActionResult<ValidatedQueryDto> Get([FromQuery] ValidatedQueryDto request) => Ok(request);

    [HttpGet("many")]
    public ActionResult<ManyFieldsDto> Many([FromQuery] ManyFieldsDto request) => Ok(request);

    [HttpGet("huge")]
    public ActionResult<HugeMessageDto> Huge([FromQuery] HugeMessageDto request) => Ok(request);

    [HttpGet("overflow")]
    public ActionResult<OverflowFieldsDto> Overflow([FromQuery] OverflowFieldsDto request) => Ok(request);
}
