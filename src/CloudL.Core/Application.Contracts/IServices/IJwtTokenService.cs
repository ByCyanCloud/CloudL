using System.Security.Claims;

namespace CloudL.Application.Contracts.IServices;

/// <summary>
/// JWT 令牌生成服务。
/// Refresh Token 的持久化与校验由 <see cref="IRefreshTokenStore"/> 负责，两者职责分离。
/// </summary>
public interface IJwtTokenService
{
    /// <summary>生成 Access Token。</summary>
    /// <remarks>可通过 <c>extraClaims</c> 追加自定义声明（如设备号、租户号），调用方自行保证不与框架预设 claim 冲突。</remarks>
    string GenerateAccessToken(
        Guid userId,
        string userCode,
        string userName,
        IEnumerable<string>? roles = null,
        string? organizationCode = null,
        IEnumerable<Claim>? extraClaims = null);

    /// <summary>生成一个高熵的 Refresh Token 字符串（不含业务含义）。</summary>
    string GenerateRefreshToken();

    /// <summary>Access Token 过期时间（小时）。</summary>
    int AccessTokenExpirationHours { get; }

    /// <summary>Refresh Token 过期时间（天）。</summary>
    int RefreshTokenExpirationDays { get; }
}
