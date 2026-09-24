using CloudL.AspNetCore.HttpApi.Binding;
using Microsoft.Extensions.Primitives;

namespace CloudL.UnitTests;

/// <summary>
/// snake_case query 参数绑定测试。
/// 这块逻辑一旦失效，调用方按文档传 <c>?page_index=2</c> 会<strong>静默</strong>被忽略（拿到默认值而不报错），
/// 所以必须有测试覆盖。
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

    [Fact]
    public void Build_ShouldKeepOriginalKeyAndAddPascalCaseAlias()
    {
        var query = new Dictionary<string, StringValues>
        {
            ["page_index"] = "2",
            ["page_size"] = "50",
            ["sort_by"] = "user_name"
        };

        var values = SnakeCaseQueryKey.Build(query);

        Assert.Equal("2", values["page_index"]);
        Assert.Equal("2", values["PageIndex"]);
        Assert.Equal("50", values["PageSize"]);
        Assert.Equal("user_name", values["SortBy"]);
    }

    [Fact]
    public void Build_ShouldPreferSnakeCaseValue_WhenBothFormsArePresent()
    {
        var query = new Dictionary<string, StringValues>
        {
            ["page_index"] = "2",
            ["pageIndex"] = "9"
        };

        var values = SnakeCaseQueryKey.Build(query);

        Assert.Equal("2", values["PageIndex"]);
    }

    [Fact]
    public void Build_ShouldLeavePlainKeysUntouched()
    {
        var query = new Dictionary<string, StringValues> { ["id"] = "abc" };

        var values = SnakeCaseQueryKey.Build(query);

        Assert.Single(values);
        Assert.Equal("abc", values["id"]);
    }

    [Fact]
    public void Build_ShouldBeCaseInsensitive_ForModelBindingLookups()
    {
        var query = new Dictionary<string, StringValues> { ["page_index"] = "3" };

        var values = SnakeCaseQueryKey.Build(query);

        Assert.Equal("3", values["PAGEINDEX"]);
    }

    [Fact]
    public void Build_ShouldThrow_ForNullInput() =>
        Assert.Throws<ArgumentNullException>(() => SnakeCaseQueryKey.Build(null!));
}
