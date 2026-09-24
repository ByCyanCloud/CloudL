namespace CloudL.Application.Contracts.IServices;

/// <summary>
/// JWT 令牌生成服务。
/// Refresh Token 的持久化与校验由 <see cref="IRefreshTokenStore"/> 负责，两者职责分离。
/// </summary>
public interface IJwtTokenService
{
    /// <summary>生成 Access Token。</summary>
    string GenerateAccessToken(
        Guid userId,
        string userCode,
        string userName,
        IEnumerable<string>? roles = null,
        string? organizationCode = null);

    /// <summary>生成一个高熵的 Refresh Token 字符串（不含业务含义）。</summary>
    string GenerateRefreshToken();

    /// <summary>Access Token 过期时间（小时）。</summary>
    int AccessTokenExpirationHours { get; }

    /// <summary>Refresh Token 过期时间（天）。</summary>
    int RefreshTokenExpirationDays { get; }
}