namespace CloudL.IntegrationTests;

/// <summary>
/// 全局异常中间件的集成测试：验证未处理异常真的被转成框架统一响应，而不是裸 500 / 开发人员异常页。
/// </summary>
public class ExceptionHandlingTests
{
    [Fact]
    public async Task UnhandledException_ShouldReturnUnifiedServerError()
    {
        await using var host = await CloudLTestHost.StartAsync();

        var response = await host.Client.GetAsync("/api/diagnostic/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.False(json["success"]!.GetValue<bool>());
        Assert.Equal(ErrorCodes.ServerError, json["status_code"]!.GetValue<int>());
    }

    [Fact]
    public async Task UnhandledException_ShouldReturnErrorIdForSupport()
    {
        await using var host = await CloudLTestHost.StartAsync();

        var response = await host.Client.GetAsync("/api/diagnostic/boom");
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        var errorId = json["error_id"]?.GetValue<string>();

        Assert.False(string.IsNullOrWhiteSpace(errorId));
    }

    [Fact]
    public async Task UnhandledException_ShouldNotLeakExceptionDetail()
    {
        await using var host = await CloudLTestHost.StartAsync();

        var response = await host.Client.GetAsync("/api/diagnostic/boom");
        var body = await response.Content.ReadAsStringAsync();

        // 异常信息只应进日志，不应出现在响应体里（否则等于对外泄露实现细节）
        Assert.DoesNotContain("集成测试故意抛出的异常", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("at CloudL", body);
    }
}
