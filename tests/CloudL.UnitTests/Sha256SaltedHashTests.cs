using System;
using System.Security.Cryptography;
using System.Text;
using CloudL.Domain.Shared.Security;
using Xunit;

namespace CloudL.UnitTests;

/// <summary>
/// 单次 SHA-256 + 随机盐的哈希：格式、随机盐、校验容错，以及<strong>拼接顺序的钉子</strong>。
/// </summary>
/// <remarks>
/// 顺序钉子用测试里<strong>独立构造</strong>的格式串来验证 —— 若实现把"盐在前"改成"明文在前"，
/// 这里构造出来的哈希就对不上，测试立刻变红（这正是存量数据会失效的那种改动）。
/// </remarks>
public class Sha256SaltedHashTests
{
    /// <summary>按文档格式独立构造一个哈希串（不复用被测的 Hash 方法）。</summary>
    private static string BuildHash(string plaintext, byte[] salt)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[salt.Length + bytes.Length];

        salt.CopyTo(buffer, 0);           // 盐在前
        bytes.CopyTo(buffer, salt.Length); // 明文在后

        return string.Join(
            '$',
            "sha256",
            Convert.ToBase64String(salt),
            Convert.ToBase64String(SHA256.HashData(buffer)));
    }

    [Fact]
    public void Hash_ShouldFollowDocumentedFormat()
    {
        var hash = Sha256SaltedHash.Hash("P@ssw0rd");

        var segments = hash.Split('$');

        Assert.Equal(3, segments.Length);
        Assert.Equal("sha256", segments[0]);
        Assert.Equal(16, Convert.FromBase64String(segments[1]).Length);
        Assert.Equal(32, Convert.FromBase64String(segments[2]).Length); // SHA-256 = 32 字节
    }

    [Fact]
    public void Hash_ShouldUseRandomSalt_SameInputDifferentResult()
    {
        var first = Sha256SaltedHash.Hash("same-input");
        var second = Sha256SaltedHash.Hash("same-input");

        Assert.NotEqual(first, second);
        Assert.True(Sha256SaltedHash.Verify("same-input", first));
        Assert.True(Sha256SaltedHash.Verify("same-input", second));
    }

    [Fact]
    public void Verify_ShouldAcceptIndependentlyBuiltHash_PinningSaltBeforePlaintext()
    {
        var salt = new byte[16];
        for (var index = 0; index < salt.Length; index++)
        {
            salt[index] = (byte)(index + 1);
        }

        var stored = BuildHash("机构密钥-abc", salt);

        Assert.True(Sha256SaltedHash.Verify("机构密钥-abc", stored));
        Assert.False(Sha256SaltedHash.Verify("机构密钥-abd", stored));
    }

    [Fact]
    public void Verify_ShouldRejectMismatchedPlaintext()
    {
        var stored = Sha256SaltedHash.Hash("correct");

        Assert.False(Sha256SaltedHash.Verify("wrong", stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash")]
    [InlineData("sha256$only-two-segments")]
    [InlineData("md5$AAAA$BBBB")]                  // 算法标识不符
    [InlineData("sha256$!!!$???")]                 // 非法 Base64
    [InlineData("sha256$AAAA$BBBB$CCCC")]          // 段数过多
    public void Verify_ShouldReturnFalse_OnMalformedInput_WithoutThrowing(string? stored)
    {
        Assert.False(Sha256SaltedHash.Verify("anything", stored!));
    }

    [Theory]
    [InlineData("sha256$AAAA$BBBB", true)]
    [InlineData("sha256$", true)]
    [InlineData("SHA256$AAAA$BBBB", false)]   // 大小写敏感（格式标识固定小写）
    [InlineData("pbkdf2$AAAA$BBBB", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsOurFormat_ShouldDetectPrefix(string? stored, bool expected)
    {
        Assert.Equal(expected, Sha256SaltedHash.IsOurFormat(stored));
    }

    [Fact]
    public void Hash_ShouldRejectEmptyPlaintext()
    {
        Assert.Throws<ArgumentException>(() => Sha256SaltedHash.Hash("  "));
    }
}
