using CloudL.AspNetCore.HttpApi.Binding;
using Microsoft.Extensions.Primitives;

namespace CloudL.UnitTests;

/// <summary>
/// snake_case query 参数绑定测试。
/// <para>这块逻辑一旦失效，调用方按文档传 <c>?page_index=2</c> 会<strong>静默</strong>被忽略（拿到默认值而不报错），
/// 或者 camelCase 被悄悄接受 —— 两者都很难排查，因此必须有测试覆盖。</para>
/// </summary>
public class SnakeCaseQueryBindingTests
{
    [Theory]
    [InlineData("page_index", "PageIndex")]
    [InlineData("page_size", "PageSize")]
    [InlineData("sort_by", "SortBy")]
    [InlineData("sort_direction", "SortDirection")]
    [InlineData("filter.page_index", "Filter.PageIndex")]
    [InlineData("filter.sort_by", "Filter.SortBy")]
    [InlineData("pageIndex", "pageIndex")]
    [InlineData("PageIndex", "PageIndex")]
    [InlineData("id", "id")]
    [InlineData("", "")]
    public void ToPascalCase_ShouldConvertOnlySnakeCaseKeys(string input, string expected) =>
        Assert.Equal(expected, SnakeCaseQueryKey.ToPascalCase(input));

    [Theory]
    [InlineData("page_index", true)]
    [InlineData("id", true)]
    [InlineData("page2", true)]
    [InlineData("filter.page_index", true)]
    [InlineData("api-version", true)]
    [InlineData("_t", true)]
    [InlineData("pageIndex", false)]
    [InlineData("PageIndex", false)]
    [InlineData("ID", false)]
    [InlineData("accessToken", false)]
    [InlineData("page index", false)]
    [InlineData("page/ index", false)]
    [InlineData("", false)]
    public void IsSnakeCase_ShouldRejectUpperCaseAndInvalidCharacters(string key, bool expected) =>
        Assert.Equal(expected, SnakeCaseQueryKey.IsSnakeCase(key));

    [Fact]
    public void Build_ShouldExposeOnlyConvertedKeys()
    {
        var query = new Dictionary<string, StringValues>
        {
            ["page_index"] = "2",
            ["page_size"] = "50",
            ["sort_by"] = "user_name"
        };

        var values = SnakeCaseQueryKey.Build(query);

        // 三个参数 → 三个键，说明没有为原始键名额外开兼容通道
        Assert.Equal(3, values.Count);
        Assert.Equal("2", values["PageIndex"]);
        Assert.Equal("50", values["PageSize"]);
        Assert.Equal("user_name", values["SortBy"]);
    }

    [Fact]
    public void Build_ShouldConvertNestedKeys()
    {
        var query = new Dictionary<string, StringValues> { ["filter.page_index"] = "3" };

        var values = SnakeCaseQueryKey.Build(query);

        Assert.Single(values);
        Assert.Equal("3", values["Filter.PageIndex"]);
    }

    [Fact]
    public void Build_ShouldKeepSingleWordKeys()
    {
        var query = new Dictionary<string, StringValues> { ["id"] = "abc" };

        var values = SnakeCaseQueryKey.Build(query);

        Assert.Single(values);
        Assert.Equal("abc", values["id"]);
    }

    [Fact]
    public void Build_ShouldPreserveMultipleValuesForSameKey()
    {
        var query = new Dictionary<string, StringValues> { ["user_ids"] = new[] { "1", "2", "3" } };

        var values = SnakeCaseQueryKey.Build(query);

        var userIds = values["UserIds"];
        Assert.Equal(3, userIds.Count);
        Assert.Equal("1", userIds[0]);
        Assert.Equal("2", userIds[1]);
        Assert.Equal("3", userIds[2]);
    }

    [Fact]
    public void Build_ShouldThrow_ForNullInput() =>
        Assert.Throws<ArgumentNullException>(() => SnakeCaseQueryKey.Build(null!));
}
