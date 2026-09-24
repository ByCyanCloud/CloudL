using CloudL.AspNetCore.Json;

namespace CloudL.UnitTests;

/// <summary>
/// 框架 JSON 约定测试。
/// 重点是<strong>不要</strong>改写字典键：那会破坏业务数据的语义（例如 <c>USD</c> → <c>usd</c>），
/// 也会让错误响应里的键与调用方实际传入的参数名不一致。
/// </summary>
public class CloudLJsonTests
{
    [Fact]
    public void SerializerOptions_ShouldNotSetDictionaryKeyPolicy() =>
        Assert.Null(CloudLJson.SerializerOptions.DictionaryKeyPolicy);

    [Fact]
    public void Serialize_ShouldKeepDictionaryKeysAsIs()
    {
        var payload = new Dictionary<string, string>
        {
            ["USD"] = "美元",
            ["pageIndex"] = "保留原样"
        };

        var json = CloudLJson.Serialize(payload);

        Assert.Contains("\"USD\"", json);
        Assert.Contains("\"pageIndex\"", json);
        Assert.DoesNotContain("\"usd\"", json);
    }

    [Fact]
    public void Serialize_ShouldConvertPropertyNamesToSnakeCase()
    {
        var json = CloudLJson.Serialize(new SampleDto { UserName = "alice", RowVersion = 3 });

        Assert.Contains("\"user_name\"", json);
        Assert.Contains("\"row_version\"", json);
    }

    [Fact]
    public void Serialize_ShouldNotEscapeChineseCharacters()
    {
        var json = CloudLJson.Serialize(new SampleDto { UserName = "张三" });

        Assert.Contains("张三", json);
        Assert.DoesNotContain("\\u", json);
    }

    private sealed class SampleDto
    {
        public string UserName { get; set; } = string.Empty;

        public int RowVersion { get; set; }
    }
}
