namespace Livia.Domain.Shared.Constants;

/// <summary>
/// 框架级全局常量。
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// 数据库字符串列的默认最大长度。
    /// 仅在实体属性<strong>未显式配置</strong> MaxLength 时生效，不会覆盖 Fluent API 中的显式配置。
    /// </summary>
    public const int DefaultStringMaxLength = 256;

    /// <summary>默认分页大小。</summary>
    public const int DefaultPageSize = 20;

    /// <summary>最大分页大小。</summary>
    public const int MaxPageSize = 100;

    /// <summary>GetAllAsync 的安全上限，防止误加载全表。</summary>
    public const int MaxGetAllCount = 1000;

    /// <summary>批量创建的最大条数。</summary>
    public const int MaxBatchCreateCount = 100;

    /// <summary>JWT Access Token 默认过期时间（小时）。</summary>
    public const int JwtTokenExpirationHours = 8;

    /// <summary>JWT Refresh Token 默认过期时间（天）。</summary>
    public const int JwtRefreshTokenExpirationDays = 7;
}