using CloudL.Application.Contracts.Dtos;
using TemplateProject.Application.Contracts.Dtos;

namespace TemplateProject.Application.Contracts.IServices;

/// <summary>
/// 用户服务接口。
/// </summary>
public interface IUserService
{
    /// <summary>根据 ID 获取用户。</summary>
    Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>分页获取用户列表。</summary>
    Task<PagedResponseDto<UserDto>> GetPagedListAsync(
        PagedRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>创建用户。</summary>
    Task<UserDto> CreateAsync(CreateUserDto input, CancellationToken cancellationToken = default);

    /// <summary>更新用户资料（乐观锁）。</summary>
    Task<UserDto> UpdateAsync(Guid id, UpdateUserDto input, CancellationToken cancellationToken = default);

    /// <summary>删除用户。</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>登录：校验凭据、必要时透明升级密码哈希、签发并保存令牌。</summary>
    Task<LoginResponseDto> LoginAsync(LoginDto input, CancellationToken cancellationToken = default);

    /// <summary>刷新令牌（一次性消费，返回新的令牌对）。</summary>
    Task<LoginResponseDto> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>登出：撤销刷新令牌。</summary>
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>修改密码：校验当前密码，更新哈希并撤销该用户全部刷新令牌。</summary>
    Task ChangePasswordAsync(Guid userId, ChangePasswordDto input, CancellationToken cancellationToken = default);
}
