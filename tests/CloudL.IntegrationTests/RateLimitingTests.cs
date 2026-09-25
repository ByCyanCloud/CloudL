namespace CloudL.IntegrationTests;

/// <summary>
/// 限流的集成测试：验证真实管道里配额生效、返回统一 429，并且分区键确实是客户端 IP。
/// </summary>
public class RateLimitingTests
{
    private const int PermitLimit = 3;

    private static Dictionary<string, string?> LimitedSettings() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Testing:UseClientIpHeader"] = "true",
        ["RateLimits:Auth:PermitLimit"] = PermitLimit.ToString(),
        ["RateLimits:Auth:WindowSeconds"] = "60"
    };

    [Fact]
    public async Task RequestsWithinLimit_ShouldSucceed()
    {
        await using var host = await CloudLTestHost.StartAsync(LimitedSettings());

        for (var index = 0; index < PermitLimit; index++)
        {
            var response = await SendAsync(host, "10.0.0.1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task ExceedingLimit_ShouldReturnUnifiedTooManyRequests()
    {
        await using var host = await CloudLTestHost.StartAsync(LimitedSettings());

        for (var index = 0; index < PermitLimit; index++)
        {
            await SendAsync(host, "10.0.0.2");
        }

        var rejected = await SendAsync(host, "10.0.0.2");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        var json = JsonNode.Parse(await rejected.Content.ReadAsStringAsync())!;
        Assert.False(json["success"]!.GetValue<bool>());
        Assert.Equal(ErrorCodes.TooManyRequests, json["status_code"]!.GetValue<int>());
    }

    [Fact]
    public async Task DifferentClientIp_ShouldHaveIndependentQuota()
    {
        await using var host = await CloudLTestHost.StartAsync(LimitedSettings());

        for (var index = 0; index < PermitLimit; index++)
        {
            await SendAsync(host, "10.0.0.3");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendAsync(host, "10.0.0.3")).StatusCode);

        // 另一个 IP 不受影响 —— 这正是"分区键是客户端 IP"的证据
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(host, "10.0.0.4")).StatusCode);
    }

    [Fact]
    public async Task EndpointWithoutPolicy_ShouldNotBeLimited()
    {
        await using var host = await CloudLTestHost.StartAsync(LimitedSettings());

        for (var index = 0; index < PermitLimit * 3; index++)
        {
            var response = await host.Client.GetAsync("/api/diagnostic/echo");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task SameIpv6Prefix64_ShouldShareQuota()
    {
        await using var host = await CloudLTestHost.StartAsync(LimitedSettings());

        for (var index = 0; index < PermitLimit; index++)
            await SendAsync(host, "2001:db8:1:1::1");

        // 同一 /64 内换地址不应拿到新配额：否则拥有一个 /64 就能绕过限流
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendAsync(host, "2001:db8:1:1::abcd")).StatusCode);
    }

    [Fact]
    public async Task DifferentIpv6Prefix64_ShouldHaveIndependentQuota()
    {
        await using var host = await CloudLTestHost.StartAsync(LimitedSettings());

        for (var index = 0; index < PermitLimit; index++)
            await SendAsync(host, "2001:db8:2:1::1");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendAsync(host, "2001:db8:2:1::2")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(host, "2001:db8:3:1::1")).StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(CloudLTestHost host, string clientIp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/diagnostic/limited");
        request.Headers.Add("X-Test-Client-IP", clientIp);

        return await host.Client.SendAsync(request);
    }
}
