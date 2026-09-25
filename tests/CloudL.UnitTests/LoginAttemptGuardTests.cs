using CloudL.AspNetCore.Configuration;
using CloudL.AspNetCore.Infrastructure.Services;
using CloudL.Domain.Shared.Constants;
using CloudL.Domain.Shared.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CloudL.UnitTests;

/// <summary>
/// 账号维度登录锁定测试。
/// <para>这条防护补的是限流中间件的盲区：中间件拿不到用户名，所以换了 IP 之后
/// 仍然可以对同一个账号无限猜测；账号维度的计数与锁定就是堵这个口子的。</para>
/// </summary>
public class LoginAttemptGuardTests
{
    [Fact]
    public void RecordFailure_BelowThreshold_ShouldNotLock()
    {
        var (guard, _) = CreateGuard(maxFailures: 3);

        Assert.False(guard.RecordFailure("alice"));
        Assert.False(guard.RecordFailure("alice"));

        guard.EnsureNotLocked("alice"); // 不应抛出
    }

    [Fact]
    public void RecordFailure_ReachingThreshold_ShouldLockWithAccountLockedCode()
    {
        var (guard, _) = CreateGuard(maxFailures: 3);

        guard.RecordFailure("alice");
        guard.RecordFailure("alice");

        Assert.True(guard.RecordFailure("alice"));

        var exception = Assert.Throws<TooManyRequestsException>(() => guard.EnsureNotLocked("alice"));
        Assert.Equal(ErrorCodes.AccountLocked, exception.BusinessCode);
    }

    [Fact]
    public void RecordFailure_WhileLocked_ShouldNotClearLockout()
    {
        var (guard, _) = CreateGuard(maxFailures: 2);

        guard.RecordFailure("heidi");
        guard.RecordFailure("heidi");

        // 锁定期内再调用 RecordFailure，不能把锁定解除（这正是"重置分支"最容易写错的地方）
        Assert.False(guard.RecordFailure("heidi"));
        Assert.Throws<TooManyRequestsException>(() => guard.EnsureNotLocked("heidi"));
    }

    [Fact]
    public void Lockout_ShouldExpireAfterConfiguredSeconds()
    {
        var (guard, clock) = CreateGuard(maxFailures: 3, lockoutSeconds: 60);

        for (var index = 0; index < 3; index++)
            guard.RecordFailure("bob");

        Assert.Throws<TooManyRequestsException>(() => guard.EnsureNotLocked("bob"));

        clock.Advance(TimeSpan.FromSeconds(61));

        guard.EnsureNotLocked("bob"); // 锁定期已过
    }

    [Fact]
    public void Reset_ShouldClearFailuresAndLockout()
    {
        var (guard, _) = CreateGuard(maxFailures: 2);

        guard.RecordFailure("carol");
        guard.RecordFailure("carol");
        Assert.Throws<TooManyRequestsException>(() => guard.EnsureNotLocked("carol"));

        guard.Reset("carol");

        guard.EnsureNotLocked("carol");
        Assert.False(guard.RecordFailure("carol"));
    }

    [Fact]
    public void FailuresOutsideWindow_ShouldNotAccumulate()
    {
        var (guard, clock) = CreateGuard(maxFailures: 3, windowSeconds: 60);

        guard.RecordFailure("dave");
        guard.RecordFailure("dave");

        clock.Advance(TimeSpan.FromSeconds(61));

        // 窗口已过 → 重新计数，这一次只是第 1 次失败
        Assert.False(guard.RecordFailure("dave"));
        guard.EnsureNotLocked("dave");
    }

    [Fact]
    public void UserNames_ShouldBeComparedCaseInsensitively()
    {
        var (guard, _) = CreateGuard(maxFailures: 2);

        guard.RecordFailure("Eve");

        // 大小写不同但属于同一账号，否则改个大小写就能绕过计数
        Assert.True(guard.RecordFailure("eve"));
    }

    [Fact]
    public void DifferentUsers_ShouldHaveIndependentCounters()
    {
        var (guard, _) = CreateGuard(maxFailures: 2);

        guard.RecordFailure("frank");
        guard.RecordFailure("frank");

        Assert.Throws<TooManyRequestsException>(() => guard.EnsureNotLocked("frank"));
        guard.EnsureNotLocked("grace"); // 另一个账号不受影响
    }

    [Fact]
    public void BlankUserName_ShouldBeIgnored()
    {
        var (guard, _) = CreateGuard(maxFailures: 1);

        Assert.False(guard.RecordFailure("   "));
        guard.EnsureNotLocked("   ");
        guard.Reset("   ");
    }

    [Fact]
    public void ExceedingTrackedAccountLimit_ShouldKeepMemoryBounded()
    {
        // 把上限压到 1000，模拟"攻击者用海量不同用户名轰炸"
        var (guard, clock) = CreateGuard(maxFailures: 5, maxTrackedAccounts: 1000);

        for (var batch = 0; batch < 6; batch++)
        {
            for (var index = 0; index < 500; index++)
                guard.RecordFailure($"attacker-{batch}-{index}");

            clock.Advance(TimeSpan.FromSeconds(31));   // 让下一次过期清理不被节流
        }

        // 累计出现 3000 个不同用户名：跟踪量必须被兜底淘汰压住，而不是线性增长到 3000。
        // 清理与淘汰本身有节流（CleanupIntervalSeconds），所以稳态会略高于硬上限（约一个批次的量级），
        // 这是刻意的取舍：宁可短暂多留一点条目，也不让"每次失败都做一次全表扫描"。
        Assert.True(
            guard.TrackedAccountCount < 2000,
            $"跟踪量应被上限压住（远小于 3000），实际为 {guard.TrackedAccountCount}");
    }

    private static (InMemoryLoginAttemptGuard Guard, MutableTimeProvider Clock) CreateGuard(
        int maxFailures = 3,
        int lockoutSeconds = 60,
        int windowSeconds = 300,
        int maxTrackedAccounts = 200_000)
    {
        var clock = new MutableTimeProvider();

        var services = new ServiceCollection();
        services.AddOptions<LoginProtectionOptions>().Configure(options =>
        {
            options.MaxFailures = maxFailures;
            options.LockoutSeconds = lockoutSeconds;
            options.FailureWindowSeconds = windowSeconds;
            options.MaxTrackedAccounts = maxTrackedAccounts;
        });

        var provider = services.BuildServiceProvider();

        var guard = new InMemoryLoginAttemptGuard(
            clock,
            provider.GetRequiredService<IOptionsMonitor<LoginProtectionOptions>>(),
            NullLogger<InMemoryLoginAttemptGuard>.Instance);

        return (guard, clock);
    }
}

/// <summary>可控时钟：用于验证锁定到期、失败计数窗口等与时间相关的行为。</summary>
internal sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
