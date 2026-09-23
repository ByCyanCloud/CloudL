using System.Security.Cryptography;
using Livia.AspNetCore.Configuration;
using Livia.AspNetCore.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Livia.UnitTests;

/// <summary>
/// PBKDF2 密码哈希测试：格式、校验、旧格式兼容与透明升级判定。
/// </summary>
public class Pbkdf2PasswordHasherTests
{
    /// <summary>测试用迭代次数取小值，避免拖慢测试；生产默认为 220000。</summary>
    private const int TestIterations = 1_000;

    [Fact]
    public void DefaultOptions_ShouldMeetOwaspRecommendationForSha512()
    {
        // OWASP 对 PBKDF2-HMAC-SHA512 的建议值不低于 210000
        Assert.True(new PasswordHasherOptions().Iterations >= 210_000);
    }

    [Fact]
    public void Hash_ShouldProduceVersionedFormat()
    {
        var hasher = CreateHasher();

        var hash = hasher.Hash("P@ssw0rd!");

        var segments = hash.Split('$');
        Assert.Equal(4, segments.Length);
        Assert.Equal("pbkdf2-sha512", segments[0]);
        Assert.Equal(TestIterations.ToString(), segments[1]);
    }

    [Fact]
    public void Verify_ShouldReturnTrue_ForCorrectPassword()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("P@ssw0rd!");

        Assert.True(hasher.Verify("P@ssw0rd!", hash));
    }

    [Fact]
    public void Verify_ShouldReturnFalse_ForWrongPassword()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("P@ssw0rd!");

        Assert.False(hasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void Hash_ShouldUseRandomSalt_SoSamePasswordProducesDifferentHashes()
    {
        var hasher = CreateHasher();

        Assert.NotEqual(hasher.Hash("same-password"), hasher.Hash("same-password"));
    }

    [Fact]
    public void NeedsRehash_ShouldReturnFalse_ForCurrentParameters()
    {
        var hasher = CreateHasher();

        Assert.False(hasher.NeedsRehash(hasher.Hash("P@ssw0rd!")));
    }

    [Fact]
    public void NeedsRehash_ShouldReturnTrue_WhenStoredIterationsAreLower()
    {
        var strong = CreateHasher(TestIterations * 2);
        var weakHash = CreateHasher(TestIterations).Hash("P@ssw0rd!");

        Assert.True(strong.NeedsRehash(weakHash));
    }

    [Fact]
    public void Verify_ShouldAcceptLegacyFormat_AndNeedsRehashShouldFlagIt()
    {
        // 框架早期版本格式：{盐HEX}.{哈希HEX}，固定 600 次迭代
        var hasher = CreateHasher();
        var legacyHash = CreateLegacyHash("P@ssw0rd!");

        Assert.True(hasher.Verify("P@ssw0rd!", legacyHash));
        Assert.True(hasher.NeedsRehash(legacyHash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha512$abc$zz$zz")]
    [InlineData("pbkdf2-sha512$1000$zzzz$zzzz")]
    [InlineData("pbkdf2-sha512$0$AABB$CCDD")]
    [InlineData("pbkdf2-sha512$99999999999$AABB$CCDD")]
    public void Verify_ShouldReturnFalseWithoutThrowing_ForMalformedHash(string malformedHash)
    {
        var hasher = CreateHasher();

        Assert.False(hasher.Verify("P@ssw0rd!", malformedHash));
    }

    private static Pbkdf2PasswordHasher CreateHasher(int iterations = TestIterations) =>
        new(Options.Create(new PasswordHasherOptions { Iterations = iterations }));

    private static string CreateLegacyHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600, HashAlgorithmName.SHA512, 32);
        return $"{Convert.ToHexString(salt)}.{Convert.ToHexString(hash)}";
    }
}
