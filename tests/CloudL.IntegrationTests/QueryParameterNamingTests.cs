namespace CloudL.IntegrationTests;

/// <summary>
/// query 参数命名校验的集成测试。
/// 这条行为之前在本地只是"手工跑一次应用验证过"，现在把它固化成自动化回归。
/// </summary>
public class QueryParameterNamingTests
{
    [Fact]
    public async Task SnakeCaseQuery_ShouldBind()
    {
        await using var host = await CloudLTestHost.StartAsync();

        var response = await host.Client.GetAsync("/api/diagnostic/echo?page_index=2&page_size=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"page_index\":2", body);
        Assert.Contains("\"page_size\":5", body);
    }

    [Fact]
    public async Task CamelCaseQuery_ShouldBeRejectedWithUnifiedBody()
    {
        await using var host = await CloudLTestHost.StartAsync();

        var response = await host.Client.GetAsync("/api/diagnostic/echo?pageIndex=2");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.False(json["success"]!.GetValue<bool>());
        Assert.Equal(ErrorCodes.ValidationError, json["status_code"]!.GetValue<int>());
        Assert.Contains("snake_case", json["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task PascalCaseQuery_ShouldBeRejected()
    {
        await using var host = await CloudLTestHost.StartAsync();

        var response = await host.Client.GetAsync("/api/diagnostic/echo?PageIndex=2");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RequestWithoutQuery_ShouldNotBeAffected()
    {
        await using var host = await CloudLTestHost.StartAsync();

        var response = await host.Client.GetAsync("/api/diagnostic/echo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
