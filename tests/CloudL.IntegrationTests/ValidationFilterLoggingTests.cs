using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace CloudL.IntegrationTests;

/// <summary>
/// 受测契约：模型验证失败时，<see cref="CloudL.AspNetCore.HttpApi.Filters.ValidationFilter"/> 的块状日志必须与
/// <c>ExceptionHandlingMiddleware</c> 的<strong>业务异常日志同构</strong> ——
/// 同样的 <c>[Response] / [Request] / [Auth] / [Body]</c> 标签与顺序、同样的脱敏，
/// 失败字段明细并入 <c>[Response]</c> 行的 <c>errors=</c>，
/// 且日志里的 <c>errorId</c> 与响应体里的 <c>error_id</c> 是<strong>同一个值</strong>。
/// </summary>
/// <remarks>
/// 走真实 MVC 管道（TestServer）+ 捕获型 logger：日志内容由过滤器真正写出的消息决定，
/// 而不是断言某个辅助方法 —— 这样"格式改了但忘了拼进模板"这类问题也能被拦住。
/// </remarks>
public class ValidationFilterLoggingTests
{
    private const string LogTitle = "请求参数验证失败";

    /// <summary>异常中间件里"业务异常"（4xx）那条日志的标题。</summary>
    private const string BusinessExceptionLogTitle = "业务异常";

    /// <summary>
    /// 空请求体 / 缺失 Authorization 头在日志里的占位符。
    /// <para>字面量而不是引用常量：这里钉的是<strong>实际写进日志的字符串</strong>，改用常量会让断言变成自证。</para>
    /// </summary>
    private const string NonePlaceholder = "(none)";

    private readonly ITestOutputHelper _output;

    public ValidationFilterLoggingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task InvalidModelState_ShouldLogBlockShapedLikeBusinessException()
    {
        var capture = new CapturingLoggerProvider();
        await using var host = await CloudLTestHost.StartAsync(loggerProvider: capture);

        var response = await host.Client.GetAsync("/api/validated-query?pageIndex=0&pageSize=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var entry = Assert.Single(CaptureValidationFailures(capture));
        var log = entry.Message;

        // 标题可辨识、级别仍是 Warning，且消息不以换行开头（否则 [级别] 前缀会独占一行）
        Assert.StartsWith(LogTitle, log, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.False(log.StartsWith('\n'), "消息不得以换行开头");
        Assert.False(log.EndsWith('\n'), "消息不得以换行结尾（本请求无 body，[Body] 段曾被渲染成空串）：" + log);
        Assert.DoesNotContain("\n\n", log, StringComparison.Ordinal);

        // 旧结构必须消失：ErrorId 独占一行、errors 独占一段
        Assert.DoesNotContain("[Errors]", log, StringComparison.Ordinal);
        Assert.DoesNotContain("[ErrorId=", log, StringComparison.Ordinal);

        // 与业务异常日志同一套标签 + 同一顺序
        Assert.True(IndexOf(log, "[Response]") < IndexOf(log, "[Request]"), "标签顺序应为 Response → Request：" + log);
        Assert.True(IndexOf(log, "[Request]") < IndexOf(log, "[Auth]"), "标签顺序应为 Request → Auth：" + log);
        Assert.True(IndexOf(log, "[Auth]") < IndexOf(log, "[Body]"), "标签顺序应为 Auth → Body：" + log);

        // errors 是统一响应体的一部分 → 必须与 statusCode/errorId/message 同行（而不是独占一段）
        var responseLine = GetResponseLine(log);
        Assert.Contains("statusCode=4000", responseLine, StringComparison.Ordinal);
        Assert.Contains(", message=请求参数验证失败, errors=", responseLine, StringComparison.Ordinal);
        Assert.Contains("page_index: 页码必须大于等于 1", responseLine, StringComparison.Ordinal);
        Assert.Contains("page_size: 每页条数必须大于等于 1", responseLine, StringComparison.Ordinal);

        // 响应结构不变：统一 ApiResponse + 错误键 snake_case（这条只是防止日志改动误伤响应）
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.False(body["success"]!.GetValue<bool>());
        Assert.Equal(ErrorCodes.ValidationError, body["status_code"]!.GetValue<int>());
        Assert.True(body["errors"]!["page_index"] is not null);

        // 日志里的 errorId 必须等于响应里的 error_id —— 否则拿着响应的 error_id 在日志里搜不到
        Assert.Equal(body["error_id"]!.GetValue<string>(), ExtractErrorId(responseLine));
    }

    [Fact]
    public async Task InvalidModelState_ShouldRedactBodyAndAuthLikeExceptionMiddleware()
    {
        var capture = new CapturingLoggerProvider();

        // 脱敏是"显式关掉开关"后的行为：Logging:RevealSensitiveDataInLogs 默认 true（明文），
        // 这条用例钉的是 false 那一档（开关的默认档由 SensitiveDataLoggingSwitchTests 覆盖）。
        await using var host = await CloudLTestHost.StartAsync(
            new Dictionary<string, string?> { ["Logging:RevealSensitiveDataInLogs"] = "false" },
            capture);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/validated-query/body")
        {
            Content = new StringContent(
                "{\"secret_key\":\"super-secret-value\"}",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.UH_mqU");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var log = Assert.Single(CaptureValidationFailures(capture)).Message;

        // 与异常中间件用同一套脱敏实现：Authorization 只留方案与末尾字符，敏感键整体遮蔽
        Assert.Contains("[Auth]\nBearer ***UH_mqU", log, StringComparison.Ordinal);
        Assert.Contains("\"secret_key\":\"***\"", log, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", log, StringComparison.Ordinal);

        // 请求体非空时消息两端都不带换行（空行由输出模板最前面的 {NewLine} 产生）
        Assert.False(log.StartsWith('\n') || log.EndsWith('\n'), "消息不得以换行开头/结尾");

        // 字段错误与请求体同框，且 errors 落在 [Response] 行
        Assert.Contains("organization_code: 机构编号不能为空", GetResponseLine(log), StringComparison.Ordinal);
    }

    /// <summary>
    /// 空请求体（无 body 的 GET）：<c>[Body]</c> 段必须渲染成非空占位符，否则整条块状日志<strong>以换行结尾</strong>。
    /// <para>这是此前遗留的缺口：只钉了"不以换行开头"，没钉"不以换行结尾"，于是空 body 一路漏过去。
    /// 同时钉死两条路径（<see cref="CloudL.AspNetCore.HttpApi.Filters.ValidationFilter"/> 与
    /// <c>ExceptionHandlingMiddleware</c>）除了标题与业务字段外<strong>逐字节一致</strong>。</para>
    /// </summary>
    [Fact]
    public async Task EmptyBody_ShouldRenderNonePlaceholderAndNeverEndWithNewLine_OnBothPaths()
    {
        var capture = new CapturingLoggerProvider();
        await using var host = await CloudLTestHost.StartAsync(loggerProvider: capture);

        // 两条路径都用"不带 body 的 GET" —— 没有请求体正是这条缺陷的触发条件。
        var validationResponse = await host.Client.GetAsync("/api/validated-query?pageIndex=0&pageSize=0");
        var exceptionResponse = await host.Client.GetAsync("/api/diagnostic/missing");

        Assert.Equal(HttpStatusCode.BadRequest, validationResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, exceptionResponse.StatusCode);

        var validationLog = Assert.Single(CaptureValidationFailures(capture)).Message;
        var exceptionLog = Assert.Single(CaptureBusinessExceptions(capture)).Message;

        _output.WriteLine("路径一（ValidationFilter）：\n" + validationLog);
        _output.WriteLine("路径二（ExceptionHandlingMiddleware）：\n" + exceptionLog);

        // 1) 两条路径都不得以换行开头 / 结尾（空请求体这一情形必须被覆盖）
        Assert.False(validationLog.StartsWith('\n'), "验证失败日志不得以换行开头：" + validationLog);
        Assert.False(validationLog.EndsWith('\n'), "验证失败日志不得以换行结尾（空 body 被渲染成了空串）：" + validationLog);
        Assert.False(exceptionLog.StartsWith('\n'), "业务异常日志不得以换行开头：" + exceptionLog);
        Assert.False(exceptionLog.EndsWith('\n'), "业务异常日志不得以换行结尾（空 body 被渲染成了空串）：" + exceptionLog);
        Assert.DoesNotContain("\n\n", validationLog, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n", exceptionLog, StringComparison.Ordinal);

        // 2) [Body] 段是同一个非空占位符，而不是空串
        Assert.Equal(NonePlaceholder, GetBlockSection(validationLog, "[Body]"));
        Assert.Equal(NonePlaceholder, GetBlockSection(exceptionLog, "[Body]"));

        // 3) 逐字节比对：抹掉"随请求而变"的部分（标题、[Response] 行、[Request] 行）之后，
        //    两条路径必须完全相同 —— [Auth] 与 [Body] 段用的脱敏与占位符也必须是同一套。
        Assert.Equal(
            NormalizeBlockForComparison(exceptionLog),
            NormalizeBlockForComparison(validationLog));
    }

    [Fact]
    public async Task ManyFailedFields_ShouldLogAllWhenUnderCap()
    {
        var capture = new CapturingLoggerProvider();
        await using var host = await CloudLTestHost.StartAsync(loggerProvider: capture);

        var response = await host.Client.GetAsync("/api/validated-query/many");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var log = Assert.Single(CaptureValidationFailures(capture)).Message;

        // 最多列 30 个字段，超出时标注总项数；明细全在 [Response] 行里。
        Assert.Equal(12, CountOccurrences(log, "字段必填"));
        Assert.Equal(12, CountOccurrences(GetResponseLine(log), "字段必填"));
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

        var log = Assert.Single(CaptureValidationFailures(capture)).Message;

        // 失败 40 个字段：日志只列前 30 个，并标注实际项数（标注同样落在 [Response] 行里）。
        Assert.Equal(30, CountOccurrences(log, "字段必填"));
        Assert.Contains("…（共 40 项）", GetResponseLine(log), StringComparison.Ordinal);
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

        var log = Assert.Single(CaptureValidationFailures(capture)).Message;

        // 单条消息长度上限 1000 字符：截断并标注，且不能把整条日志撑爆。
        Assert.Contains("…（已截断", log, StringComparison.Ordinal);

        var responseLine = GetResponseLine(log);
        var errorsValue = responseLine[(responseLine.IndexOf("errors=", StringComparison.Ordinal) + "errors=".Length)..];

        // 截断标注必须落在 [Response] 行的 errors 明细里（而不是另起一段）
        Assert.Contains("…（已截断", responseLine, StringComparison.Ordinal);

        // 1000 字符明细 + "…（已截断 N 字符）" 标注
        Assert.True(errorsValue.Length <= 1000 + 40, $"errors 明细未被限长：{errorsValue.Length}");

        // 整条日志 = errors 上限 + 固定骨架（[Response]/[Request]/[Auth]/[Body] 约 200 字符）
        Assert.True(log.Length < 1000 + 400, "日志未被限长，实际长度：" + log.Length);
    }

    /// <summary>只挑出验证失败那条日志 —— 宿主/限流等其它日志不参与断言。</summary>
    private static CapturedLog[] CaptureValidationFailures(CapturingLoggerProvider capture) =>
        capture.Entries
            .Where(entry => entry.Message.Contains(LogTitle, StringComparison.Ordinal))
            .ToArray();

    /// <summary>只挑出异常中间件写的"业务异常"那条日志（4xx 分支）。</summary>
    private static CapturedLog[] CaptureBusinessExceptions(CapturingLoggerProvider capture) =>
        capture.Entries
            .Where(entry => entry.Message.StartsWith(BusinessExceptionLogTitle, StringComparison.Ordinal))
            .ToArray();

    /// <summary>
    /// 取出某个段标签之后的全部内容（不含标签本身，也不含紧随标签的换行），
    /// 用于比对两条路径的同名段是否<strong>逐字节</strong>相同。
    /// </summary>
    private static string GetBlockSection(string log, string tag)
    {
        var start = IndexOf(log, tag) + tag.Length;

        // 段内容到下一个段标签（"\n[" 起头）或消息末尾为止
        var next = log.IndexOf("\n[", start, StringComparison.Ordinal);
        var end = next >= 0 ? next : log.Length;

        return log[start..end].TrimStart('\n');
    }

    /// <summary>
    /// 把块状日志里"随请求而变"的部分抹成占位符：标题行、<c>[Response]</c> 行的载荷、<c>[Request]</c> 行的载荷。
    /// 剩下的标签、顺序、以及 <c>[Auth]</c>/<c>[Body]</c> 段的实际内容必须两条路径逐字节相同。
    /// </summary>
    private static string NormalizeBlockForComparison(string log)
    {
        var lines = log.Split('\n');

        // 第 0 行是标题（"请求参数验证失败" vs "业务异常"），本来就该不同
        lines[0] = "<title>";

        for (var index = 1; index < lines.Length - 1; index++)
        {
            if (lines[index] is "[Response]" or "[Request]")
            {
                // 这两行的载荷是业务字段（状态码/errorId/message/路径），允许不同
                lines[index + 1] = "<payload>";
                index++;
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>取出块状日志里 <c>[Response]</c> 的下一行 —— 失败明细必须和 statusCode/errorId 同行。</summary>
    private static string GetResponseLine(string log)
    {
        var lines = log.Split('\n');
        var index = Array.IndexOf(lines, "[Response]");

        Assert.True(index >= 0 && index + 1 < lines.Length, "日志里没有 [Response] 行：" + log);

        return lines[index + 1];
    }

    /// <summary>取出 <c>[Response]</c> 行里的 <c>errorId</c> 值。</summary>
    private static string ExtractErrorId(string responseLine)
    {
        var match = Regex.Match(responseLine, @"errorId=(?<id>[0-9a-f]{32})");

        Assert.True(match.Success, "未在 [Response] 行里找到 errorId：" + responseLine);

        return match.Groups["id"].Value;
    }

    /// <summary>断言标签存在并返回位置，用于校验块状日志的顺序。</summary>
    private static int IndexOf(string log, string marker)
    {
        var index = log.IndexOf(marker, StringComparison.Ordinal);

        Assert.True(index >= 0, $"日志里缺少 {marker} 段：" + log);

        return index;
    }

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

    /// <summary>一条被捕获的日志：级别 + 渲染后的消息。</summary>
    private sealed record CapturedLog(LogLevel Level, string Message);

    /// <summary>把所有日志消息与级别收集起来，供断言使用。</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
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

/// <summary>两个字段都带约束：验证失败时日志里应同时出现两个字段名。</summary>
public sealed class ValidatedQueryDto
{
    [Range(1, int.MaxValue, ErrorMessage = "页码必须大于等于 1")]
    public int PageIndex { get; set; }

    [Range(1, 100, ErrorMessage = "每页条数必须大于等于 1")]
    public int PageSize { get; set; }
}

/// <summary>POST 请求体：一个必填字段 + 一个敏感字段（用于验证 [Body] 脱敏）。</summary>
public sealed class ValidatedBodyDto
{
    [Required(ErrorMessage = "机构编号不能为空")]
    public string? OrganizationCode { get; set; }

    public string? SecretKey { get; set; }
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

    [HttpPost("body")]
    public ActionResult<ValidatedBodyDto> Body([FromBody] ValidatedBodyDto request) => Ok(request);

    [HttpGet("many")]
    public ActionResult<ManyFieldsDto> Many([FromQuery] ManyFieldsDto request) => Ok(request);

    [HttpGet("huge")]
    public ActionResult<HugeMessageDto> Huge([FromQuery] HugeMessageDto request) => Ok(request);

    [HttpGet("overflow")]
    public ActionResult<OverflowFieldsDto> Overflow([FromQuery] OverflowFieldsDto request) => Ok(request);
}
