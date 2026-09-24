namespace CloudL.Application.Contracts.IServices;

/// <summary>
/// Refresh Token 存储抽象。
/// 默认实现为进程内存储，适用于单实例或开发环境；
/// 多实例部署时应替换为 Redis / 数据库实现（注册自己的 <see cref="IRefreshTokenStore"/> 即可覆盖）。
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>保存 Refresh Token 及其归属用户与过期时间。</summary>
    Task StoreAsync(
        string refreshToken,
        Guid userId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验并<strong>一次性消费</strong>（旋转）Refresh Token。
    /// 无效、已过期或已被使用过时返回 <c>null</c>。
    /// </summary>
    Task<Guid?> ConsumeAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>撤销单个 Refresh Token（登出）。</summary>
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>撤销某用户的全部 Refresh Token（改密、强制下线）。</summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}