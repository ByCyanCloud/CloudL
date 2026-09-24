using CloudL.EntityFrameworkCore.Repositories;

namespace CloudL.IntegrationTests;

/// <summary>
/// 分页测试。
/// <para>重点保护一处<strong>真实修复</strong>：只按业务字段排序时，并列值之间的顺序在 SQL 中未定义，
/// 会导致翻页出现重复行或漏行 —— 因此分页查询必须追加主键作为次级排序键。</para>
/// </summary>
public class PaginationTests
{
    [Fact]
    public async Task GeneratedSql_ShouldOrderByBusinessFieldThenPrimaryKey()
    {
        await using var environment = await TransactionTestEnvironment.CreateAsync();
        await using var context = environment.CreateContext();
        var repository = new EfCoreRepository<TestOrder, Guid>(context);

        environment.SqlStatements.Clear();
        await repository.GetPagedAsync(1, 10, orderBy: entity => entity.CreatedAt);

        var sql = environment.SqlStatements
            .FirstOrDefault(statement => statement.Contains("ORDER BY", StringComparison.Ordinal));

        Assert.NotNull(sql);

        var orderByIndex = sql.IndexOf("ORDER BY", StringComparison.Ordinal);
        var createdAtIndex = sql.IndexOf("CreatedAt", orderByIndex, StringComparison.Ordinal);
        var idIndex = sql.IndexOf("Id", orderByIndex, StringComparison.Ordinal);

        Assert.True(createdAtIndex >= 0, $"分页 SQL 未按业务字段排序：{sql}");
        Assert.True(
            idIndex > createdAtIndex,
            $"分页 SQL 缺少主键次级排序键（并列 CreatedAt 时翻页会重复或漏行）：{sql}");
    }

    [Fact]
    public async Task PagingThroughTiedOrderValues_ShouldReturnEveryRowExactlyOnce()
    {
        await using var environment = await TransactionTestEnvironment.CreateAsync();
        await using var context = environment.CreateContext();
        var repository = new EfCoreRepository<TestOrder, Guid>(context);

        // 10 条同一 CreatedAt（形成并列值：没有次级排序键时翻页就可能错乱）
        for (var index = 0; index < 10; index++)
        {
            context.Orders.Add(new TestOrder(Guid.NewGuid(), $"order-{index}"));
        }

        await context.SaveChangesAsync();

        var seen = new List<Guid>();

        for (var pageIndex = 1; pageIndex <= 4; pageIndex++)
        {
            var page = await repository.GetPagedAsync(pageIndex, 3, orderBy: entity => entity.CreatedAt);
            seen.AddRange(page.Items.Select(item => item.Id));
        }

        Assert.Equal(10, seen.Count);
        Assert.Equal(10, seen.Distinct().Count());
    }

    [Fact]
    public async Task GetPagedAsync_ShouldRespectPageSizeAndTotalCount()
    {
        await using var environment = await TransactionTestEnvironment.CreateAsync();
        await using var context = environment.CreateContext();
        var repository = new EfCoreRepository<TestOrder, Guid>(context);

        for (var index = 0; index < 7; index++)
        {
            context.Orders.Add(new TestOrder(Guid.NewGuid(), $"order-{index}"));
        }

        await context.SaveChangesAsync();

        var firstPage = await repository.GetPagedAsync(1, 3);
        var lastPage = await repository.GetPagedAsync(3, 3);

        Assert.Equal(3, firstPage.Items.Count);
        Assert.Single(lastPage.Items);
        Assert.Equal(7, firstPage.TotalCount);
    }
}
