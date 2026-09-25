namespace CloudL.AspNetCore.Configuration;

/// <summary>
/// 密码哈希配置，对应配置节 <c>PasswordHasher</c>。
/// 迭代次数可随硬件升级而提高，旧哈希会在用户下次登录成功时自动透明升级。
/// </summary>
public class PasswordHasherOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "PasswordHasher";

    /// <summary>
    /// PBKDF2-HMAC-SHA512 迭代次数。默认 220000，符合 OWASP 对该算法的当前建议。
    /// 调高后无需迁移数据：旧哈希仍可校验，并在登录成功后按新参数重新哈希。
    /// </summary>
    public int Iterations { get; set; } = 220_000;
}
