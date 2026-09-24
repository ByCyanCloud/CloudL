using System.Reflection;
using CloudL.Application.Contracts.IServices;
using CloudL.Domain.DomainEvents;
using CloudL.Domain.Entities;
using CloudL.Domain.Shared.Constants;
using CloudL.Domain.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace CloudL.EntityFrameworkCore;

/// <summary>
/// 框架 DbContext 基类。
/// 业务侧只需派生并声明自己的 DbSet，即可获得：
/// <list type="bullet">
///   <item>审计字段自动填充（CreatedBy / UpdatedAt / UpdatedBy）；</item>
///   <item>乐观锁令牌自动推进与并发冲突转换为 <see cref="ConcurrencyConflictException"/>；</item>
///   <item>领域事件的收集、分发与清空；</item>
///   <item>数据库字符串列默认长度约定（<strong>仅对未显式配置的属性生效</strong>）。</item>
/// </list>
/// </summary>
/// <remarks>
/// 关于领域事件的一致性：事件在 <c>SaveChanges</c> <strong>提交成功之后</strong>分发。
/// 此时数据已落库，若处理器抛异常，调用方会收到失败响应但数据已提交。
/// 对强一致性有要求的场景，请在业务侧引入 Outbox 模式，或覆写
/// <see cref="DispatchDomainEventsAsync"/> 改变分发时机。
/// </remarks>
public abstract class FrameworkDbContext : DbContext
{
    private readonly IDomainEventDispatcher? _domainEventDispatcher;
    private readonly ICurrentUser? _currentUser;

    protected FrameworkDbContext(
        DbContextOptions options,
        IDomainEventDispatcher? domainEventDispatcher = null,
        ICurrentUser? currentUser = null)
        : base(options)
    {
        _domainEventDispatcher = domainEventDispatcher;
        _currentUser = currentUser;
    }

    /// <summary>
    /// 实体配置（<see cref="IEntityTypeConfiguration{TEntity}"/>）所在程序集。
    /// 默认取派生 Context 所在程序集；若业务把实体配置放在独立程序集，覆写此属性即可。
    /// </summary>
    protected virtual Assembly ConfigurationAssembly => GetType().Assembly;

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesCoreAsync(acceptAllChangesOnSuccess, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default) =>
        SaveChangesCoreAsync(acceptAllChangesOnSuccess, cancellationToken);

    /// <summary>在模型构建完成后应用实体配置与全局约定。</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(ConfigurationAssembly);
        ApplyDefaultStringConventions(modelBuilder);
    }

    /// <summary>
    /// 分发领域事件。默认交由 <see cref="IDomainEventDispatcher"/> 处理；
    /// 覆写此方法可改变分发时机或接入 Outbox。
    /// </summary>
    protected virtual async Task DispatchDomainEventsAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents,
        CancellationToken cancellationToken)
    {
        if (_domainEventDispatcher is null || domainEvents.Count == 0)
            return;

        await _domainEventDispatcher.DispatchAsync(domainEvents, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> SaveChangesCoreAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken)
    {
        // 1. 先收集领域事件：此时实体仍处于 Added/Modified 状态，事件最完整
        var domainEvents = ChangeTracker.Entries<BaseEntity>()
            .SelectMany(entry => entry.Entity.DomainEvents)
            .ToArray();

        // 2. 填充审计字段与并发令牌
        ApplyAuditFields();

        int affectedRows;
        try
        {
            // 3. 持久化
            affectedRows = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("数据已被其他用户修改，请刷新后重试", ex);
        }
        finally
        {
            // 无论成功失败都清空，避免下次保存时重复分发
            ClearDomainEvents();
        }

        // 4. 提交成功后再分发领域事件
        await DispatchDomainEventsAsync(domainEvents, cancellationToken).ConfigureAwait(false);

        return affectedRows;
    }

    private void ApplyAuditFields()
    {
        var userId = _currentUser?.UserId;
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (userId.HasValue)
                        entry.Entity.CreatedBy = userId;
                    break;

                case EntityState.Modified:
                    entry.Entity.RowVersion = Guid.NewGuid();
                    entry.Entity.UpdatedAt = now;
                    if (userId.HasValue)
                        entry.Entity.UpdatedBy = userId;
                    break;
            }
        }
    }

    private void ClearDomainEvents()
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            entry.Entity.ClearDomainEvents();
        }
    }

    /// <summary>
    /// 为字符串列应用默认长度与 Unicode 约定。
    /// 关键点：仅当属性<strong>未显式配置</strong> MaxLength 时才套用默认值，
    /// 因此实体配置里的 <c>HasMaxLength(32)</c> 等不会被覆盖。
    /// </summary>
    private static void ApplyDefaultStringConventions(ModelBuilder modelBuilder)
    {
        var stringProperties = modelBuilder.Model
            .GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => property.ClrType == typeof(string));

        foreach (var property in stringProperties)
        {
            if (property.GetMaxLength() is null)
            {
                property.SetMaxLength(AppConstants.DefaultStringMaxLength);
            }

            if (property.IsUnicode() is null)
            {
                property.SetIsUnicode(true);
            }
        }
    }
}