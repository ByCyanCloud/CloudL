using CloudL.Application.Contracts.IServices;
using CloudL.Domain.DomainEvents;
using CloudL.Domain.Entities;
using CloudL.EntityFrameworkCore;
using CloudL.EntityFrameworkCore.EntityConfigurations;
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

/// <summary>可审计实体：用于验证审计字段自动填充与乐观锁。</summary>
internal sealed class TestCustomer : AuditableEntity<Guid>
{
    private TestCustomer()
    {
    }

    public TestCustomer(Guid id, string name)
        : base(id)
    {
        Name = name;
    }

    public string Name { get; private set; } = string.Empty;

    public void Rename(string name) => Name = name;
}

/// <summary>
/// 实体配置：<strong>必须继承框架的配置基类</strong>，RowVersion 才会被设为并发令牌。
/// 否则乐观锁测试会"假通过" —— 数据库层根本没有令牌可比对。
/// </summary>
internal sealed class TestCustomerConfiguration : AuditableEntityConfiguration<TestCustomer>
{
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

/// <summary>当前用户桩：用于验证审计字段是否写入正确的人。</summary>
internal sealed class TestCurrentUser : ICurrentUser
{
    public TestCurrentUser(Guid? userId) => UserId = userId;

    public bool IsAuthenticated => UserId.HasValue;

    public Guid? UserId { get; }

    public string? UserCode => null;

    public string? UserName => UserId.HasValue ? "tester" : null;

    public string? OrganizationCode => null;

    public IReadOnlyList<string> Roles => [];

    public bool HasRole(string roleCode) => false;

    public bool HasAnyRole(params string[] roleCodes) => false;
}

/// <summary>测试用 DbContext（SQLite 内存库）。</summary>
internal sealed class TestDbContext : FrameworkDbContext
{
    public TestDbContext(
        DbContextOptions<TestDbContext> options,
        IDomainEventDispatcher dispatcher,
        ICurrentUser? currentUser = null)
        : base(options, dispatcher, currentUser)
    {
    }

    public DbSet<TestOrder> Orders => Set<TestOrder>();

    public DbSet<TestCustomer> Customers => Set<TestCustomer>();
}

/// <summary>
/// 一套"内存 SQLite + DbContext + 工作单元"的测试环境。
/// 连接全程保持打开，这样上下文与事务都作用在同一个内存库上；
/// 同时捕获 EF 生成的 SQL，供分页排序这类"看 SQL 才能验证"的断言使用。
/// </summary>
internal sealed class TransactionTestEnvironment : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ICurrentUser? _currentUser;

    private TransactionTestEnvironment(
        SqliteConnection connection,
        RecordingDomainEventDispatcher dispatcher,
        ICurrentUser? currentUser)
    {
        _connection = connection;
        Dispatcher = dispatcher;
        _currentUser = currentUser;
    }

    public RecordingDomainEventDispatcher Dispatcher { get; }

    /// <summary>EF 输出的日志（含执行的 SQL）。</summary>
    public List<string> SqlStatements { get; } = [];

    public static async Task<TransactionTestEnvironment> CreateAsync(Guid? currentUserId = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var environment = new TransactionTestEnvironment(
            connection,
            new RecordingDomainEventDispatcher(),
            currentUserId.HasValue ? new TestCurrentUser(currentUserId) : null);

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
            .LogTo(SqlStatements.Add)
            .Options;

        return new TestDbContext(options, Dispatcher, _currentUser);
    }

    /// <summary>用全新上下文统计订单数，避免读到自己变更跟踪器里的缓存。</summary>
    public async Task<int> CountOrdersAsync()
    {
        await using var context = CreateContext();
        return await context.Orders.CountAsync();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
