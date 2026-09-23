using Livia.Application.Contracts.IServices;
using Livia.Domain.DomainEvents;
using Livia.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TemplateProject.Domain.Entities;

namespace TemplateProject.EntityFrameworkCore;

/// <summary>
/// 业务数据库上下文。
/// 只需继承框架的 <see cref="FrameworkDbContext"/> 并声明自己的 DbSet，
/// 即可获得审计字段自动填充、乐观锁推进、领域事件分发与字符串列默认长度约定。
/// </summary>
/// <remarks>
/// 构造函数的 <c>IDomainEventDispatcher</c> 与 <c>ICurrentUser</c> 由 DI 自动注入；
/// 两者声明为可选参数，便于设计时工具（dotnet ef）直接实例化。
/// </remarks>
public class AppDbContext : FrameworkDbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        IDomainEventDispatcher? domainEventDispatcher = null,
        ICurrentUser? currentUser = null)
        : base(options, domainEventDispatcher, currentUser)
    {
    }

    /// <summary>用户。</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>角色。</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>用户-角色关联。</summary>
    public DbSet<UserRole> UserRoles => Set<UserRole>();
}
