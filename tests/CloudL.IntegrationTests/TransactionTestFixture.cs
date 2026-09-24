using CloudL.Domain.DomainEvents;
using CloudL.Domain.Entities;
using CloudL.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CloudL.IntegrationTests;

/// <summary>事务测试用实体：创建与改名时登记领域事件。</summary>
internal sealed class TestOrder : Entity<Guid>
{
    private TestOrder()
    {
    }

    public TestOrder(Guid id, string name)
        : base(id)
    {
        Name = name;
        AddDomainEvent(new TestOrderChanged(id, name));
    }

    public string Name { get; private set; } = string.Empty;

    public void Rename(string name)
    {
        Name = name;
        AddDomainEvent(new TestOrderChanged(Id, name));
    }
}

/// <summary>事务测试用领域事件。</summary>
internal sealed record TestOrderChanged(Guid OrderId, string Name) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>记录分发过的事件：用于断言"提交后才分发、回滚不分发"。</summary>
internal sealed class RecordingDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly List<IDomainEvent> _dispatched = [];

    public IReadOnlyList<IDomainEvent> Dispatched => _dispatched;

    public Task DispatchAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        _dispatched.AddRange(domainEvents);
        return Task.CompletedTask;
    }
}

/// <summary>事务测试用 DbContext（SQLite 内存库）。</summary>
internal sealed class TestDbContext : FrameworkDbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options, IDomainEventDispatcher dispatcher)
        : base(options, dispatcher)
    {
    }

    public DbSet<TestOrder> Orders => Set<TestOrder>();
}

/// <summary>
/// 一套"内存 SQLite + DbContext + 工作单元"的测试环境。
/// 连接全程保持打开，这样上下文与事务都作用在同一个内存库上。
/// </summary>
internal sealed class TransactionTestEnvironment : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private TransactionTestEnvironment(SqliteConnection connection, RecordingDomainEventDispatcher dispatcher)
    {
        _connection = connection;
        Dispatcher = dispatcher;
    }

    public RecordingDomainEventDispatcher Dispatcher { get; }

    public static async Task<TransactionTestEnvironment> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var environment = new TransactionTestEnvironment(connection, new RecordingDomainEventDispatcher());

        await using (var context = environment.CreateContext())
        {
            await context.Database.EnsureCreatedAsync();
        }

        return environment;
    }

    public TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new TestDbContext(options, Dispatcher);
    }

    /// <summary>用全新上下文统计订单数，避免读到自己变更跟踪器里的缓存。</summary>
    public async Task<int> CountOrdersAsync()
    {
        await using var context = CreateContext();
        return await context.Orders.CountAsync();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
