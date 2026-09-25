namespace CloudL.Application.Contracts.IServices;

/// <summary>
/// 密码哈希服务。
/// 实现须采用带工作因子的算法（当前默认 PBKDF2-HMAC-SHA512），并把算法与参数写入哈希串，
/// 以便在不破坏存量数据的前提下逐步提升强度。
/// </summary>
public interface IPasswordHasher
{
    /// <summary>对明文密码生成哈希串（含算法、参数与随机盐）。</summary>
    string Hash(string password);

    /// <summary>校验明文密码与哈希串是否匹配。</summary>
    bool Verify(string password, string hashedPassword);

    /// <summary>
    /// 判断已存储的哈希是否使用了弱于当前策略的参数。
    /// 返回 <c>true</c> 时，调用方应在用户登录成功后重新哈希并保存，实现透明升级。
    /// </summary>
    bool NeedsRehash(string hashedPassword);
}
