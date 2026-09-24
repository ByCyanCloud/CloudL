using CloudL.AspNetCore.Logging;

namespace CloudL.UnitTests;

/// <summary>
/// 日志脱敏测试。
/// 这是一个<strong>安全控件</strong>：它失效不会报错，只会静默泄露，因此必须有测试兜住。
/// </summary>
public class SensitiveDataRedactorTests
{
    [Fact]
    public void RedactBody_ShouldMaskKnownSensitiveKeys()
    {
        const string body = """
            {"user_name":"alice","password":"P@ssw0rd!","refreshToken":"abc123","clientSecret":"s3cret"}
            """;

        var redacted = SensitiveDataRedactor.RedactBody(body);

        Assert.DoesNotContain("P@ssw0rd!", redacted);
        Assert.DoesNotContain("abc123", redacted);
        Assert.DoesNotContain("s3cret", redacted);
        Assert.Contains("\"password\":\"***\"", redacted);
        Assert.Contains("\"refreshToken\":\"***\"", redacted);
    }

    [Fact]
    public void RedactBody_ShouldKeepNonSensitiveFields()
    {
        const string body = """
            {"user_name":"alice","email":"alice@example.com","page_size":20}
            """;

        var redacted = SensitiveDataRedactor.RedactBody(body);

        Assert.Contains("alice", redacted);
        Assert.Contains("alice@example.com", redacted);
        Assert.Contains("20", redacted);
    }

    [Fact]
    public void RedactBody_ShouldMaskNestedObjectsAndArrays()
    {
        const string body = """
            {"profile":{"security":{"password":"inner-secret"}},"items":[{"token":"array-secret"}]}
            """;

        var redacted = SensitiveDataRedactor.RedactBody(body);

        Assert.DoesNotContain("inner-secret", redacted);
        Assert.DoesNotContain("array-secret", redacted);
    }

    [Theory]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U")]
    [InlineData("A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q7r8S9t0")]
    public void RedactBody_ShouldMaskSecretShapedValues_EvenWhenKeyNameIsInnocent(string secretValue)
    {
        // 键名故意起成看不出敏感的字段，验证"按值形态识别"这一层
        var body = $$"""
            {"ticket":"{{secretValue}}","note":"正常备注"}
            """;

        var redacted = SensitiveDataRedactor.RedactBody(body);

        Assert.DoesNotContain(secretValue, redacted);
        Assert.Contains("正常备注", redacted);
    }

    [Fact]
    public void RedactBody_ShouldNotMaskOrdinaryLongText()
    {
        const string body = """
            {"description":"这是一段比较长的普通描述文本，不应该被误遮蔽。"}
            """;

        var redacted = SensitiveDataRedactor.RedactBody(body);

        Assert.Contains("这是一段比较长的普通描述文本", redacted);
    }

    [Fact]
    public void RedactBody_ShouldOmitNonJsonBody_WithoutLeakingContent()
    {
        const string body = "user=alice&password=P%40ssw0rd";

        var redacted = SensitiveDataRedactor.RedactBody(body);

        Assert.DoesNotContain("P%40ssw0rd", redacted);
        Assert.Contains("非 JSON 请求体已省略", redacted);
    }

    [Fact]
    public void RedactBody_ShouldNotLeakTailOfOverlongBody()
    {
        var body = "{\"data\":\"" + new string('x', 5000) + "\",\"password\":\"tail-secret\"}";

        var redacted = SensitiveDataRedactor.RedactBody(body);

        Assert.DoesNotContain("tail-secret", redacted);
    }

    [Fact]
    public void RedactBody_ShouldReturnEmpty_ForEmptyInput()
    {
        Assert.Equal(string.Empty, SensitiveDataRedactor.RedactBody(null));
        Assert.Equal(string.Empty, SensitiveDataRedactor.RedactBody("   "));
    }

    [Fact]
    public void RedactAuthorizationHeader_ShouldKeepSchemeAndTailButNotWholeToken()
    {
        const string header = "Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature";

        var redacted = SensitiveDataRedactor.RedactAuthorizationHeader(header);

        Assert.StartsWith("Bearer ***", redacted);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", redacted);
    }

    [Fact]
    public void RedactAuthorizationHeader_ShouldHandleMissingOrOpaqueValues()
    {
        Assert.Equal("(none)", SensitiveDataRedactor.RedactAuthorizationHeader(null));
        Assert.Equal("(none)", SensitiveDataRedactor.RedactAuthorizationHeader("   "));
        Assert.Equal("***", SensitiveDataRedactor.RedactAuthorizationHeader("somelongtoken"));
    }
}
