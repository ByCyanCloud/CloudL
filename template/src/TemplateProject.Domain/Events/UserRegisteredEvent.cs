using Livia.Domain.DomainEvents;

namespace TemplateProject.Domain.Events;

/// <summary>
/// 用户注册领域事件。
/// 由聚合根在构造函数中登记，框架的 <c>FrameworkDbContext</c> 会在 SaveChanges 成功后自动分发；
/// 处理器实现 <c>IDomainEventHandler&lt;UserRegisteredEvent&gt;</c> 并注册到 DI 即可。
/// </summary>
/// <param name="UserId">用户 ID。</param>
/// <param name="UserName">用户名。</param>
/// <param name="Email">邮箱。</param>
public sealed record UserRegisteredEvent(Guid UserId, string UserName, string? Email) : IDomainEvent
{
    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
