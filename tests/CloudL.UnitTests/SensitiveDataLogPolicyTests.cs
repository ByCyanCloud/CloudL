using CloudL.AspNetCore.Configuration;
using CloudL.AspNetCore.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CloudL.UnitTests;

/// <summary>
/// 日志敏感信息开关（<c>Logging:RevealSensitiveDataInLogs</c>）的策略测试。
/// <para>受测契约：<c>true</c> = 明文（<c>[Auth]</c>/<c>[Body]</c>/query 都按原文）、<c>false</c> = 恢复两层脱敏；
/// 空值（空体、缺失的头）在两种模式下都是 <c>(none)</c>；<strong>不配置该键时默认明文</strong>。</para>
/// </summary>
public class SensitiveDataLogPolicyTests
{
    /// <summary>JWT 形状的令牌（脱敏模式按"值的形态"命中）。</summary>
    private const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.UH_mqU";

    private const string BodyWithSecret =
        """{"user_name":"alice","secret_key":"super-secret-value","note":"hello"}""";

    private const string QueryWithToken = "?page_index=2&access_token=abc123&sort_by=user_name";

    [Fact]
    public void Default_WhenKeyIsAbsent_ShouldRevealSensitiveData()
    {
        // 1) 常量与属性默认值都是 true（用户明确要求的默认行为）
        Assert.True(SensitiveDataLoggingOptions.DefaultRevealSensitiveDataInLogs);
        Assert.True(new SensitiveDataLoggingOptions().RevealSensitiveDataInLogs);

        // 2) 绑定一个"没有该键"的配置节后，策略仍然是明文 —— 这才是"不配置也走明文"的实证
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // 故意只放标准日志设置：模拟存量项目里原有的 appsettings.json
                ["Logging:LogLevel:Default"] = "Information"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<SensitiveDataLoggingOptions>()
            .Bind(configuration.GetSection(SensitiveDataLoggingOptions.SectionName));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SensitiveDataLoggingOptions>>();

        Assert.True(options.Value.RevealSensitiveDataInLogs);
        Assert.True(new SensitiveDataLogPolicy(options).RevealSensitiveData);
    }

    [Fact]
    public void RevealMode_ShouldLogPlaintextAndNeverMask()
    {
        var policy = CreatePolicy(reveal: true);

        var auth = policy.FormatAuthorizationHeader("Bearer " + Token);
        var body = policy.FormatBody(BodyWithSecret);
        var query = policy.FormatQueryString(QueryWithToken);

        // 令牌原文出现在三种渲染结果里（不再有 ***）
        Assert.Equal("Bearer " + Token, auth);
        Assert.Contains("super-secret-value", body, StringComparison.Ordinal);
        Assert.Contains("access_token=abc123", query, StringComparison.Ordinal);
        Assert.DoesNotContain("***", auth, StringComparison.Ordinal);
        Assert.DoesNotContain("***", body, StringComparison.Ordinal);
        Assert.DoesNotContain("***", query, StringComparison.Ordinal);

        // 明文模式同样是单行（块状日志的消息不得以换行开头/结尾）
        Assert.DoesNotContain("\n", body, StringComparison.Ordinal);
        Assert.False(body.StartsWith('\n') || body.EndsWith('\n'));
    }

    [Fact]
    public void RedactMode_ShouldRestoreTwoLayerMasking()
    {
        var policy = CreatePolicy(reveal: false);

        var auth = policy.FormatAuthorizationHeader("Bearer " + Token);
        var body = policy.FormatBody(BodyWithSecret);
        var query = policy.FormatQueryString(QueryWithToken);

        Assert.Equal("Bearer ***UH_mqU", auth);
        Assert.Contains("\"secret_key\":\"***\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", body, StringComparison.Ordinal);
        Assert.Contains("access_token=***", query, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", query, StringComparison.Ordinal);

        // 非敏感字段在脱敏模式下也保持原样（不是"整条打码"）
        Assert.Contains("\"note\":\"hello\"", body, StringComparison.Ordinal);
        Assert.Contains("page_index=2", query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyValues_ShouldRenderNonePlaceholderInBothModes(bool reveal)
    {
        var policy = CreatePolicy(reveal);

        Assert.Equal(SensitiveDataRedactor.NonePlaceholder, policy.FormatBody(null));
        Assert.Equal(SensitiveDataRedactor.NonePlaceholder, policy.FormatBody("   "));
        Assert.Equal(SensitiveDataRedactor.NonePlaceholder, policy.FormatAuthorizationHeader(null));
        Assert.Equal(SensitiveDataRedactor.NonePlaceholder, policy.FormatAuthorizationHeader("  "));
        Assert.Equal("(none)", policy.FormatBody(string.Empty));

        // query 的空值语义与脱敏实现一致：空串（不是 "(none)"）
        Assert.Equal(string.Empty, policy.FormatQueryString(null));
        Assert.Equal(string.Empty, policy.FormatQueryString("   "));
    }

    [Fact]
    public void RevealMode_ShouldStillTruncateBodyAndQuery()
    {
        var policy = CreatePolicy(reveal: true);

        // 字面量而不是引用常量：这里钉的是"实际写进日志的字符串"
        const string truncationMarker = "...(已截断)";

        var hugeBody = "{\"password\":\"" + new string('x', 5000) + "\"}";
        var hugeQuery = "?access_token=" + new string('y', 2000);

        var body = policy.FormatBody(hugeBody);
        var query = policy.FormatQueryString(hugeQuery);

        Assert.True(body.Length < hugeBody.Length, "明文模式也必须有长度上限，否则日志会被灌爆");
        Assert.EndsWith(truncationMarker, body, StringComparison.Ordinal);
        Assert.True(query.Length < hugeQuery.Length);
        Assert.EndsWith(truncationMarker, query, StringComparison.Ordinal);
    }

    private static ISensitiveDataLogPolicy CreatePolicy(bool reveal) =>
        new SensitiveDataLogPolicy(Options.Create(new SensitiveDataLoggingOptions
        {
            RevealSensitiveDataInLogs = reveal
        }));
}
