namespace CloudL.IntegrationTests;

/// <summary>
/// 异常 → HTTP 状态码 / 业务码 的映射测试。
/// <para>这里锁定的是一条<strong>安全边界</strong>：只有框架定义的业务异常才把消息透传给调用方；
/// BCL 异常（EF Core 内部校验、LINQ <c>Single()</c>、字典取值失败……）几乎都是代码缺陷，
/// 必须报成 500 而不是 400/404，否则既误导调用方又泄露实现细节。</para>
/// </summary>
public class ExceptionMappingTests
{
    [Theory]
    [InlineData("/api/diagnostic/missing", HttpStatusCode.NotFound, ErrorCodes.NotFound)]
    [InlineData("/api/diagnostic/denied", HttpStatusCode.Forbidden, ErrorCodes.Forbidden)]
    [InlineData("/api/diagnostic/boom", HttpStatusCode.InternalServerError, ErrorCodes.ServerError)]
    public async Task Exception_ShouldMapToExpectedStatusAndBusinessCode(
        string path,
        HttpStatusCode expectedStatus,
        int expectedBusinessCode)
    {
        await using var host = await CloudLTestHost.StartAsync();

        var response = await host.Client.GetAsync(path);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedBusinessCode, json["status_code"]!.GetValue<int>());
        Assert.False(json["success"]!.GetValue<bool>());
    }

    [Fact]
    public async Task BclException_ShouldNotBeTreatedAsClientError()
    {
        await using var host = await CloudLTestHost.StartAsync();

        var response = await host.Client.GetAsync("/api/diagnostic/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task BusinessExceptionMessage_ShouldBePassedThrough()
    {
        await using var host = await CloudLTestHost.StartAsync();

        var response = await host.Client.GetAsync("/api/diagnostic/missing");

        // 业务异常的消息是写给调用方看的，必须原样返回
        Assert.Contains("集成测试：资源不存在", await response.Content.ReadAsStringAsync());
    }
}
