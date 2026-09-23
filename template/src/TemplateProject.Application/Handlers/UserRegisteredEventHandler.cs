using Livia.Application.Contracts.IServices;
using Livia.Domain.DomainEvents;
using Microsoft.Extensions.Logging;
using TemplateProject.Domain.Events;

namespace TemplateProject.Application.Handlers;

/// <summary>
/// 用户注册领域事件处理器（示例）。
/// 在 <c>ProjectApplicationModule</c> 中通过 <c>AddDomainEventHandler</c> 注册，
/// 由框架的 <c>DomainEventDispatcher</c> 在 SaveChanges 成功后自动调用。
/// </summary>
public sealed class UserRegisteredEventHandler : IDomainEventHandler<UserRegisteredEvent>
{
    private readonly ILogger<UserRegisteredEventHandler> _logger;

    public UserRegisteredEventHandler(ILogger<UserRegisteredEventHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task HandleAsync(UserRegisteredEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        // 实际项目中可在此发送欢迎邮件、初始化用户默认数据、写入审计日志等。
        _logger.LogInformation(
            "新用户已注册: {UserId} / {UserName} / {Email}",
            domainEvent.UserId, domainEvent.UserName, domainEvent.Email ?? "(未填写)");

        return Task.CompletedTask;
    }
}
