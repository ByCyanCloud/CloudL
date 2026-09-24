using CloudL.EntityFrameworkCore.Repositories;

namespace CloudL.IntegrationTests;

/// <summary>
/// 事务助手（<c>IUnitOfWork.ExecuteInTransactionAsync</c>）的集成测试。
/// <para>锁定的关键语义：<strong>领域事件在事务提交之后才分发</strong> ——
/// 回滚掉的工作不会产生任何事件，也不会留下数据。</para>
/// </summary>
public class TransactionTests
{
    [Fact]
    public async Task Commit_ShouldPersistDataAndDispatchEvents()
    {
        await using var environment = await TransactionTestEnvironment.CreateAsync();
        await using var context = environment.CreateContext();
        var unitOfWork = new EfCoreUnitOfWork(context);

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            context.Orders.Add(new TestOrder(Guid.NewGuid(), "order-1"));
            await unitOfWork.SaveChangesAsync(token);
        });

        Assert.Equal(1, await environment.CountOrdersAsync());
        Assert.Single(environment.Dispatcher.Dispatched);
    }

    [Fact]
    public async Task Rollback_ShouldNotPersistDataNorDispatchEvents()
    {
        await using var environment = await TransactionTestEnvironment.CreateAsync();
        await using var context = environment.CreateContext();
        var unitOfWork = new EfCoreUnitOfWork(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                context.Orders.Add(new TestOrder(Guid.NewGuid(), "order-1"));
                await unitOfWork.SaveChangesAsync(token);

                throw new InvalidOperationException("业务操作失败");
            }));

        Assert.Equal(0, await environment.CountOrdersAsync());

        // 关键：SaveChanges 已经执行过，但因为事务回滚，事件绝不能分发出去
        Assert.Empty(environment.Dispatcher.Dispatched);
    }

    [Fact]
    public async Task Nested_ShouldReuseOuterTransaction_AndRollbackTogether()
    {
        await using var environment = await TransactionTestEnvironment.CreateAsync();
        await using var context = environment.CreateContext();
        var unitOfWork = new EfCoreUnitOfWork(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWork.ExecuteInTransactionAsync(async outerToken =>
            {
                context.Orders.Add(new TestOrder(Guid.NewGuid(), "outer"));
                await unitOfWork.SaveChangesAsync(outerToken);

                // 内层复用外层事务：内层"提交"不能让外层数据落库
                await unitOfWork.ExecuteInTransactionAsync(
                    async innerToken =>
                    {
                        context.Orders.Add(new TestOrder(Guid.NewGuid(), "inner"));
                        await unitOfWork.SaveChangesAsync(innerToken);

                        throw new InvalidOperationException("内层失败");
                    },
                    outerToken);
            }));

        Assert.Equal(0, await environment.CountOrdersAsync());
        Assert.Empty(environment.Dispatcher.Dispatched);
    }

    [Fact]
    public async Task Nested_ShouldDispatchEventsOnce_AfterOuterCommit()
    {
        await using var environment = await TransactionTestEnvironment.CreateAsync();
        await using var context = environment.CreateContext();
        var unitOfWork = new EfCoreUnitOfWork(context);

        await unitOfWork.ExecuteInTransactionAsync(async outerToken =>
        {
            context.Orders.Add(new TestOrder(Guid.NewGuid(), "outer"));
            await unitOfWork.SaveChangesAsync(outerToken);

            await unitOfWork.ExecuteInTransactionAsync(
                async innerToken =>
                {
                    context.Orders.Add(new TestOrder(Guid.NewGuid(), "inner"));
                    await unitOfWork.SaveChangesAsync(innerToken);
                },
                outerToken);
        });

        Assert.Equal(2, await environment.CountOrdersAsync());
        Assert.Equal(2, environment.Dispatcher.Dispatched.Count);
    }

    [Fact]
    public async Task GenericOverload_ShouldReturnResult()
    {
        await using var environment = await TransactionTestEnvironment.CreateAsync();
        await using var context = environment.CreateContext();
        var unitOfWork = new EfCoreUnitOfWork(context);
        var orderId = Guid.NewGuid();

        var result = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            context.Orders.Add(new TestOrder(orderId, "order-1"));
            await unitOfWork.SaveChangesAsync(token);

            return orderId;
        });

        Assert.Equal(orderId, result);
        Assert.Equal(1, await environment.CountOrdersAsync());
    }

    [Fact]
    public async Task SaveChangesOutsideTransaction_ShouldStillDispatchImmediately()
    {
        // 回归保护：不在事务里时保持原有行为（保存后立刻分发）
        await using var environment = await TransactionTestEnvironment.CreateAsync();
        await using var context = environment.CreateContext();
        var unitOfWork = new EfCoreUnitOfWork(context);

        context.Orders.Add(new TestOrder(Guid.NewGuid(), "order-1"));
        await unitOfWork.SaveChangesAsync();

        Assert.Single(environment.Dispatcher.Dispatched);
    }
}
