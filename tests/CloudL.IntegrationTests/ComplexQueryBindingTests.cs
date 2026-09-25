using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// 受测契约：query 参数使用 camelCase（MVC 大小写不敏感绑定），响应 JSON 仍为 snake_case。
/// </summary>
public class ComplexQueryBindingTests
{
    [Fact]
    public async Task ComplexQueryObject_ShouldBindFromCamelCaseKeys()
    {
        await using var host = await CloudLTestHost.StartAsync();

        var response = await host.Client.GetAsync(
            "/api/complex-query?pageIndex=2&pageSize=5&sortBy=user_name&sortDirection=asc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement;

        Assert.Equal(2, data.GetProperty("page_index").GetInt32());
        Assert.Equal(5, data.GetProperty("page_size").GetInt32());
        Assert.Equal("user_name", data.GetProperty("sort_by").GetString());
        Assert.Equal("asc", data.GetProperty("sort_direction").GetString());
    }

    [Fact]
    public async Task SimpleQueryParameter_ShouldBindFromCamelCase()
    {
        await using var host = await CloudLTestHost.StartAsync();

        var response = await host.Client.GetAsync("/api/diagnostic/echo?pageIndex=3&pageSize=7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class ComplexQueryDto
{
    public int PageIndex { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public string? SortBy { get; set; }

    public string? SortDirection { get; set; }
}

[ApiController]
[Route("api/complex-query")]
public sealed class ComplexQueryController : ControllerBase
{
    [HttpGet]
    public ActionResult<ComplexQueryDto> Get([FromQuery] ComplexQueryDto request) => Ok(request);
}
