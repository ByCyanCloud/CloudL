using System.Globalization;
using System.Security.Cryptography;
using CloudL.Application.Contracts.IServices;
using CloudL.AspNetCore.Configuration;
using Microsoft.Extensions.Options;

namespace CloudL.AspNetCore.Infrastructure.Services;

/// <summary>
/// PBKDF2-HMAC-SHA512 密码哈希实现。
/// <para>哈希串格式：<c>pbkdf2-sha512${迭代次数}${盐HEX}${哈希HEX}</c>，算法与参数随哈希一起存储，
/// 因此可以安全地提升迭代次数而不影响存量数据。</para>
/// <para>兼容框架早期版本的两段格式 <c>{盐HEX}.{哈希HEX}</c>（600 次迭代），
/// 并在用户下次登录成功时通过 <see cref="NeedsRehash"/> 触发透明升级。</para>
/// </summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Algorithm = "pbkdf2-sha512";
    private const char SegmentSeparator = '$';
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int LegacyIterations = 600;

    /// <summary>迭代次数上限，防止被篡改的哈希串造成 CPU 拒绝服务。</summary>
    private const int MaxSupportedIterations = 10_000_000;

    private readonly int _iterations;

    public Pbkdf2PasswordHasher(IOptions<PasswordHasherOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _iterations = options.Value.Iterations > 0
            ? options.Value.Iterations
            : new PasswordHasherOptions().Iterations;
    }

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            _iterations,
            HashAlgorithmName.SHA512,
            HashSize);

        return string.Join(
            SegmentSeparator,
            Algorithm,
            _iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToHexString(salt),
            Convert.ToHexString(hash));
    }

    /// <inheritdoc />
    public bool Verify(string password, string hashedPassword)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword))
            return false;

        if (hashedPassword.StartsWith(Algorithm + SegmentSeparator, StringComparison.Ordinal))
            return VerifyCurrentFormat(password, hashedPassword);

        // 兼容早期两段格式：{盐HEX}.{哈希HEX}
        return VerifyLegacyFormat(password, hashedPassword);
    }

    /// <inheritdoc />
    public bool NeedsRehash(string hashedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword))
            return true;

        if (!hashedPassword.StartsWith(Algorithm + SegmentSeparator, StringComparison.Ordinal))
            return true;

        return !TryParseCurrentFormat(hashedPassword, out var iterations, out _, out _)
            || iterations < _iterations;
    }

    private static bool VerifyCurrentFormat(string password, string hashedPassword)
    {
        if (!TryParseCurrentFormat(hashedPassword, out var iterations, out var salt, out var expectedHash))
            return false;

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA512,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    private static bool VerifyLegacyFormat(string password, string hashedPassword)
    {
        var parts = hashedPassword.Split('.');
        if (parts.Length != 2)
            return false;

        try
        {
            var salt = Convert.FromHexString(parts[0]);
            var expectedHash = Convert.FromHexString(parts[1]);
            if (expectedHash.Length == 0)
                return false;

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                LegacyIterations,
                HashAlgorithmName.SHA512,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }
        catch (FormatException)
        {
            // 非法十六进制：视为校验失败，而不是抛出 500
            return false;
        }
    }

    private static bool TryParseCurrentFormat(
        string hashedPassword,
        out int iterations,
        out byte[] salt,
        out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];

        var parts = hashedPassword.Split(SegmentSeparator);
        if (parts.Length != 4)
            return false;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out iterations))
            return false;

        if (iterations is < 1 or > MaxSupportedIterations)
            return false;

        try
        {
            salt = Convert.FromHexString(parts[2]);
            hash = Convert.FromHexString(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length > 0 && hash.Length > 0;
    }
}
