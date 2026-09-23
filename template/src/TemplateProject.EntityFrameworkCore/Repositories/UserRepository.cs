using Livia.EntityFrameworkCore.Repositories;
using Microsoft.EntityFrameworkCore;
using TemplateProject.Domain.Entities;
using TemplateProject.Domain.Repositories;

namespace TemplateProject.EntityFrameworkCore.Repositories;

/// <summary>
/// 用户仓储实现：在框架通用仓储之上补充需要显式 Include 的查询。
/// </summary>
public class UserRepository : EfCoreRepository<User, Guid>, IUserRepository
{
    public UserRepository(AppDbContext dbContext)
        : base(dbContext)
    {
    }

    /// <inheritdoc />
    public async Task<User?> GetByIdWithRolesAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<User?> FindByUserNameWithRolesAsync(
        string userName,
        CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(user => user.UserName == userName, cancellationToken)
            .ConfigureAwait(false);
}
