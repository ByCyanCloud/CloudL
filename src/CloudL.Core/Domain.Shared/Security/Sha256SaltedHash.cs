using System.Security.Cryptography;
using System.Text;

namespace CloudL.Domain.Shared.Security;

/// <summary>
/// 「单次 SHA-256 + 每行随机盐」的哈希实现：<c>SHA256(盐 ‖ UTF8(明文))</c>。
/// </summary>
/// <remarks>
/// <para>存储格式：<c>sha256$&lt;Base64(16 字节盐)&gt;$&lt;Base64(哈希)&gt;</c>。</para>
/// <para><strong>用途</strong>：为兼容存量数据而提供的算法实现，也适用于"库泄露后可接受离线穷举"的场景
/// （例如机构密钥）。用户登录口令请使用框架默认的 PBKDF2（<c>IPasswordHasher</c> 默认实现）——
/// 若存量口令是本算法产生的，请在校验通过后<strong>重新用 PBKDF2 存储</strong>
/// （见 <c>IPasswordHasher.NeedsRehash</c>），即"存量兼容 + 登录时自动升级"。</para>
/// <para>⚠️ <strong>本算法不带工作因子</strong>（没有迭代次数），**不是**通用的口令哈希工具：
/// 单轮 SHA-256 对现代硬件而言穷举成本很低。</para>
/// <para><strong>兼容性警告</strong>：存储格式与"盐在前、明文在后"的拼接顺序一经确定<strong>不可更改</strong> ——
/// 改动会让所有存量哈希失效。测试里有钉子用例固定这一点。</para>
/// </remarks>
public static class Sha256SaltedHash
{
    /// <summary>算法标识（存储格式的第一段）。</summary>
    public const string AlgorithmName = "sha256";

    private const char Separator = '$';
    private const int SaltBytes = 16;

    /// <summary>生成哈希串。自带随机盐，因此同一明文每次结果都不同。</summary>
    public static string Hash(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);

        return string.Join(
            Separator,
            AlgorithmName,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(ComputeHash(plaintext, salt)));
    }

    /// <summary>校验明文是否与存储的哈希串匹配。格式非法时返回 <c>false</c>，不抛异常。</summary>
    public static bool Verify(string plaintext, string storedHash)
    {
        if (string.IsNullOrEmpty(plaintext) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        var segments = storedHash.Split(Separator);

        if (segments.Length != 3
            || !string.Equals(segments[0], AlgorithmName, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(segments[1]);
            expected = Convert.FromBase64String(segments[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        // 定长比较：避免按字节短路比较泄漏信息
        return CryptographicOperations.FixedTimeEquals(ComputeHash(plaintext, salt), expected);
    }

    /// <summary>判断给定字符串是否是本方案的哈希串。</summary>
    public static bool IsOurFormat(string? storedHash) =>
        !string.IsNullOrWhiteSpace(storedHash)
        && storedHash.StartsWith(AlgorithmName + Separator, StringComparison.Ordinal);

    /// <summary><c>SHA256(盐 ‖ UTF8(明文))</c>。</summary>
    private static byte[] ComputeHash(string plaintext, byte[] salt)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[salt.Length + bytes.Length];

        salt.CopyTo(buffer, 0);
        bytes.CopyTo(buffer, salt.Length);

        return SHA256.HashData(buffer);
    }
}
