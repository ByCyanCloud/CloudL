namespace CloudL.IntegrationTests;

/// <summary>
/// 账号维度锁定（<c>ILoginAttemptGuard</c>）的集成测试：验证在真实管道里，
/// 连续登录失败真的会把账号锁住并返回统一 429（业务码 4291）。
/// </summary>
public class LoginLockoutTests
{
    private const int MaxFailures = 3;

    /// <summary>把 IP 限流放宽，避免它干扰账号维度的测试（两个维度是独立的）。</summary>
    private static Dictionary<string, string?> Settings() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["LoginProtection:MaxFailures"] = MaxFailures.ToString(),
        ["LoginProtection:LockoutSeconds"] = "60",
        ["RateLimits:Auth:PermitLimit"] = "100"
    };

    [Fact]
    public async Task ReachingFailureThreshold_ShouldLockAccount()
    {
        await using var host = await CloudLTestHost.StartAsync(Settings());

        for (var index = 0; index < MaxFailures; index++)
        {
            var failure = await LoginFailureAsync(host, "alice");
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        var locked = await LoginFailureAsync(host, "alice");

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);

        var json = JsonNode.Parse(await locked.Content.ReadAsStringAsync())!;
        Assert.False(json["success"]!.GetValue<bool>());
        Assert.Equal(ErrorCodes.AccountLocked, json["status_code"]!.GetValue<int>());
    }

    [Fact]
    public async Task LockedAccount_ShouldNotAffectOtherAccounts()
    {
        await using var host = await CloudLTestHost.StartAsync(Settings());

        for (var index = 0; index < MaxFailures; index++)
        {
            await LoginFailureAsync(host, "bob");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginFailureAsync(host, "bob")).StatusCode);

        // 另一个账号照常走认证失败流程
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFailureAsync(host, "carol")).StatusCode);
    }

    [Fact]
    public async Task SuccessfulLogin_ShouldClearFailureCount()
    {
        await using var host = await CloudLTestHost.StartAsync(Settings());

        await LoginFailureAsync(host, "dave");
        await LoginFailureAsync(host, "dave");

        var success = await host.Client.PostAsync("/api/diagnostic/login-success?user_name=dave", null);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);

        // 计数已清零：再失败两次仍然只是"失败"，不会被上一次的计数顶到阈值
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFailureAsync(host, "dave")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFailureAsync(host, "dave")).StatusCode);
    }

    [Fact]
    public async Task UserNameCase_ShouldNotBypassLockout()
    {
        await using var host = await CloudLTestHost.StartAsync(Settings());

        for (var index = 0; index < MaxFailures; index++)
        {
            await LoginFailureAsync(host, "Eve");
        }

        // 换个大小写不应绕过锁定
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginFailureAsync(host, "eve")).StatusCode);
    }

    private static Task<HttpResponseMessage> LoginFailureAsync(CloudLTestHost host, string userName) =>
        host.Client.PostAsync($"/api/diagnostic/login-failure?user_name={userName}", null);
}
