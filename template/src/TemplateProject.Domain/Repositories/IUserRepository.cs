using CloudL.Domain.Repositories;
using TemplateProject.Domain.Entities;

namespace TemplateProject.Domain.Repositories;

/// <summary>
/// 用户仓储接口：在框架通用仓储之上补充需要显式加载关联的查询。
/// </summary>
public interface IUserRepository : IRepository<User, Guid>
{
    /// <summary>根据 ID 获取用户（含角色）。</summary>
    Task<User?> GetByIdWithRolesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>根据用户名获取用户（含角色），用于登录。</summary>
    Task<User?> FindByUserNameWithRolesAsync(string userName, CancellationToken cancellationToken = default);
}
