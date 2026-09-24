using CloudL.Domain.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace CloudL.IntegrationTests;

/// <summary>
/// 审计字段与乐观锁的集成测试。
/// <para>这两项都是 <c>FrameworkDbContext</c> 的"隐形能力"：之前完全没有覆盖，
/// 改坏了不会有任何提示，等业务数据脏了才发现。</para>
/// </summary>
public class AuditAndConcurrencyTests
{
    private static readonly Guid CurrentUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Create_ShouldFillCreatedByOnly()
    {
        await using var environment = await TransactionTestEnvironment.CreateAsync(CurrentUserId);
        await using var context = environment.CreateContext();

        var customer = new TestCustomer(Guid.NewGuid(), "alice");
        context.Customers.Add(customer);

        await context.SaveChangesAsync();

        Assert.Equal(CurrentUserId, customer.CreatedBy);
        Assert.Null(customer.UpdatedAt);
        Assert.Null(customer.UpdatedBy);
    }

    [Fact]
    public async Task Update_ShouldFillUpdatedFieldsAndAdvanceRowVersion()
    {
        await using var environment = await TransactionTestEnvironment.CreateAsync(CurrentUserId);
        var id = Guid.NewGuid();

        await using (var seed = environment.CreateContext())
        {
            seed.Customers.Add(new TestCustomer(id, "alice"));
            await seed.SaveChangesAsync();
        }

        await using var context = environment.CreateContext();
        var customer = await context.Customers.SingleAsync(item => item.Id == id);
        var versionBefore = customer.RowVersion;

        customer.Rename("alice-2");

        await context.SaveChangesAsync();

        Assert.Equal(CurrentUserId, customer.UpdatedBy);
        Assert.NotNull(customer.UpdatedAt);
        Assert.NotEqual(versionBefore, customer.RowVersion);
    }

    [Fact]
    public async Task ConcurrentUpdate_ShouldBeTranslatedToConcurrencyConflict()
    {
        await using var environment = await TransactionTestEnvironment.CreateAsync(CurrentUserId);
        var id = Guid.NewGuid();

        await using (var seed = environment.CreateContext())
        {
            seed.Customers.Add(new TestCustomer(id, "alice"));
            await seed.SaveChangesAsync();
        }

        await using var first = environment.CreateContext();
        await using var second = environment.CreateContext();

        var firstCopy = await first.Customers.SingleAsync(item => item.Id == id);
        var secondCopy = await second.Customers.SingleAsync(item => item.Id == id);

        firstCopy.Rename("from-first");
        await first.SaveChangesAsync();

        secondCopy.Rename("from-second");

        // 并发令牌已被别人推进 → 必须转成框架的业务异常（409），而不是裸的 DbUpdateConcurrencyException
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task WithoutCurrentUser_ShouldNotWriteAuditUser()
    {
        await using var environment = await TransactionTestEnvironment.CreateAsync();
        await using var context = environment.CreateContext();

        var customer = new TestCustomer(Guid.NewGuid(), "bob");
        context.Customers.Add(customer);

        await context.SaveChangesAsync();

        // 没有当前用户时不应写入脏的 CreatedBy
        Assert.Null(customer.CreatedBy);
    }
}
