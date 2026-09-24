using System.Collections.Concurrent;
using CloudL.Application.Contracts.IServices;
using CloudL.AspNetCore.Configuration;
using CloudL.Domain.Shared.Constants;
using CloudL.Domain.Shared.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudL.AspNetCore.Infrastructure.Services;

/// <summary>
/// 进程内登录失败计数与临时锁定。
/// </summary>
/// <remarks>
/// <strong>限制（务必知晓）</strong>：状态保存在单个进程内。
/// <list type="bullet">
///   <item>多实例部署时每个实例各算一套计数，实际允许的尝试次数会成倍放大 ——
///         此时应替换为分布式实现（如 Redis），接口不变。</item>
///   <item>攻击者用随机用户名轰炸会撑大内部字典，因此达到阈值后会做一次过期清理。</item>
/// </list>
/// </remarks>
public sealed class InMemoryLoginAttemptGuard : ILoginAttemptGuard
{
    /// <summary>超过该条目数时触发一次过期清理，避免随机用户名轰炸导致内存增长。</summary>
    private const int SweepThreshold = 50_000;

    private readonly ConcurrentDictionary<string, AttemptState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly TimeProvider _timeProvider;
    private readonly IOptionsMonitor<LoginProtectionOptions> _options;
    private readonly ILogger<InMemoryLoginAttemptGuard> _logger;

    public InMemoryLoginAttemptGuard(
        TimeProvider timeProvider,
        IOptionsMonitor<LoginProtectionOptions> options,
        ILogger<InMemoryLoginAttemptGuard> logger)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public void EnsureNotLocked(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return;

        var state = GetState(userName);
        var remaining = GetRemainingLockout(state, _timeProvider.GetUtcNow());

        if (remaining <= TimeSpan.Zero)
            return;

        var remainingSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
        _logger.LogWarning(
            "账号处于登录锁定期，拒绝登录尝试: {UserName}，剩余 {RemainingSeconds} 秒",
            userName, remainingSeconds);

        throw new TooManyRequestsException(
            ErrorCodes.AccountLocked,
            $"登录失败次数过多，请 {remainingSeconds} 秒后再试");
    }

    /// <inheritdoc />
    public bool RecordFailure(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return false;

        var options = _options.CurrentValue;
        var now = _timeProvider.GetUtcNow();

        SweepIfNeeded(now, options);

        var state = GetState(userName);
        var window = TimeSpan.FromSeconds(options.FailureWindowSeconds);

        lock (state)
        {
            // 锁定期内不接受新的计数，也绝不能因此解除锁定
            if (GetRemainingLockout(state, now) > TimeSpan.Zero)
                return false;

            // 计数窗口已过（或上次锁定刚结束）→ 重新从 1 开始
            if (state.FirstFailureAt is null || now - state.FirstFailureAt.Value > window)
            {
                state.FailureCount = 1;
                state.FirstFailureAt = now;
                state.LockedUntil = default;

                return false;
            }

            state.FailureCount++;

            if (state.FailureCount < options.MaxFailures)
                return false;

            state.FailureCount = 0;
            state.FirstFailureAt = null;
            state.LockedUntil = now.AddSeconds(options.LockoutSeconds);

            _logger.LogWarning(
                "账号因连续登录失败被临时锁定 {LockoutSeconds} 秒: {UserName}",
                options.LockoutSeconds, userName);

            return true;
        }
    }

    /// <inheritdoc />
    public void Reset(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return;

        _states.TryRemove(userName, out _);
    }

    private static TimeSpan GetRemainingLockout(AttemptState state, DateTimeOffset now) =>
        state.LockedUntil > now ? state.LockedUntil - now : TimeSpan.Zero;

    private AttemptState GetState(string userName) =>
        _states.GetOrAdd(userName, static _ => new AttemptState());

    private void SweepIfNeeded(DateTimeOffset now, LoginProtectionOptions options)
    {
        if (_states.Count < SweepThreshold)
            return;

        var window = TimeSpan.FromSeconds(options.FailureWindowSeconds);

        foreach (var pair in _states)
        {
            var state = pair.Value;

            lock (state)
            {
                var idle = GetRemainingLockout(state, now) <= TimeSpan.Zero
                    && (state.FirstFailureAt is null || now - state.FirstFailureAt.Value > window);

                if (idle)
                    _states.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed class AttemptState
    {
        public int FailureCount { get; set; }

        public DateTimeOffset? FirstFailureAt { get; set; }

        public DateTimeOffset LockedUntil { get; set; }
    }
}
