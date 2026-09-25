using CloudL.AspNetCore.Configuration;
using CloudL.AspNetCore.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace CloudL.UnitTests;

/// <summary>
/// 密码哈希的健壮性测试。
/// <para>哈希串来自数据库（历史遗留、迁移、人工改动、损坏都可能），因此**任何畸形输入都必须返回"校验失败"**，
/// 而不是抛异常 —— 否则一条脏数据就会把登录接口变成 500。</para>
/// </summary>
public class PasswordHasherRobustnessTests
{
    [Theory]
    // ---- 当前格式 ----
    [InlineData("not-a-hash")]                                                          // 完全不是哈希
    [InlineData("pbkdf2-sha512$220000$abcd")]                                           // 段数不足
    [InlineData("pbkdf2-sha512$abc$00112233445566778899aabbccddeeff$0011")]             // 迭代次数非数字
    [InlineData("pbkdf2-sha512$0$00112233445566778899aabbccddeeff$0011")]               // 迭代次数 0
    [InlineData("pbkdf2-sha512$99999999$00112233445566778899aabbccddeeff$0011")]        // 迭代次数超上限
    [InlineData("pbkdf2-sha512$220000$$0011")]                                          // 空盐
    [InlineData("pbkdf2-sha512$220000$0102$0011")]                                      // 盐过短（2 字节）
    [InlineData("pbkdf2-sha512$220000$zz$0011")]                                        // 非法十六进制
    [InlineData("pbkdf2-sha512$220000$00112233445566778899aabbccddeeff$")]              // 空哈希
    // ---- 旧的两段格式 ----
    [InlineData(".00112233")]                                                           // 空盐
    [InlineData("0102.00112233")]                                                       // 盐过短
    [InlineData("zz.00112233")]                                                         // 非法十六进制
    [InlineData("a.b.c")]                                                               // 段数不对
    public void Verify_ShouldReturnFalse_ForMalformedHash(string malformedHash)
    {
        var hasher = CreateHasher();

        var result = hasher.Verify("any-password", malformedHash);

        Assert.False(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha512$220000$00112233445566778899aabbccddeeff$0011")]
    public void Verify_ShouldReturnFalse_ForEmptyPassword(string hashedPassword)
    {
        var hasher = CreateHasher();

        Assert.False(hasher.Verify(string.Empty, hashedPassword));
    }

    [Fact]
    public void Verify_ShouldStillAcceptAValidHash()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("P@ssw0rd!");

        Assert.True(hasher.Verify("P@ssw0rd!", hash));
        Assert.False(hasher.Verify("wrong-password", hash));
    }

    private static Pbkdf2PasswordHasher CreateHasher() =>
        new(Options.Create(new PasswordHasherOptions()));
}
