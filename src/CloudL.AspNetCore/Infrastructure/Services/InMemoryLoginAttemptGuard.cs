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
/// 多实例部署时每个实例各算一套计数，实际允许的尝试次数会成倍放大 ——
/// 此时应替换为分布式实现（如 Redis），接口不变。
/// </remarks>
public sealed class InMemoryLoginAttemptGuard : ILoginAttemptGuard
{
    private readonly ConcurrentDictionary<string, AttemptState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly TimeProvider _timeProvider;
    private readonly IOptionsMonitor<LoginProtectionOptions> _options;
    private readonly ILogger<InMemoryLoginAttemptGuard> _logger;

    /// <summary>上次过期清理时间：用来给清理做时间节流。</summary>
    private DateTimeOffset _lastCleanupAt = DateTimeOffset.MinValue;

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

    /// <summary>诊断用：当前跟踪的账号数（验证"海量用户名不会撑爆内存"）。</summary>
    public int TrackedAccountCount => _states.Count;

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

        CleanupIfNeeded(now, options);

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

    /// <summary>
    /// 防止"随机用户名轰炸"把内存撑爆。
    /// </summary>
    /// <remarks>
    /// <para>两个关键点，缺一个都会把防护本身变成攻击面：</para>
    /// <list type="number">
    ///   <item><strong>按时间节流</strong>：如果每次失败都全表扫描，高基数时它就是个 CPU 放大器；</item>
    ///   <item><strong>硬上限兜底</strong>：过期清理只清得掉"窗口外"的条目，若攻击者持续用新用户名轰炸，
    ///         条目都还在窗口内，清理释放不出任何空间 —— 必须有按最久未活动淘汰的兜底。</item>
    /// </list>
    /// </remarks>
    private void CleanupIfNeeded(DateTimeOffset now, LoginProtectionOptions options)
    {
        // 达到上限一半时才考虑清理，且两次清理之间有最小间隔
        var cleanupThreshold = Math.Max(1000, options.MaxTrackedAccounts / 2);

        if (_states.Count < cleanupThreshold)
            return;

        if (now - _lastCleanupAt < TimeSpan.FromSeconds(options.CleanupIntervalSeconds))
            return;

        _lastCleanupAt = now;

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

        if (_states.Count >= options.MaxTrackedAccounts)
            EvictLeastRecentlyActive(now, options);
    }

    /// <summary>
    /// 兜底淘汰：宁可丢失少量锁定计数（这些账号下次失败会重新计数），也不能让内存无限增长。
    /// </summary>
    private void EvictLeastRecentlyActive(DateTimeOffset now, LoginProtectionOptions options)
    {
        var target = options.MaxTrackedAccounts * 3 / 4;
        var removeCount = _states.Count - target;

        if (removeCount <= 0)
            return;

        var victims = _states
            .OrderBy(pair => GetLastActivity(pair.Value, now))
            .Take(removeCount)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in victims)
            _states.TryRemove(key, out _);

        _logger.LogWarning(
            "登录失败计数条目达到上限，已淘汰最久未活动的 {Count} 条（唯一用户名数量异常，可能存在撞库攻击）",
            victims.Length);
    }

    private static DateTimeOffset GetLastActivity(AttemptState state, DateTimeOffset now) =>
        state.LockedUntil > now
            ? state.LockedUntil
            : state.FirstFailureAt ?? DateTimeOffset.MinValue;

    private sealed class AttemptState
    {
        public int FailureCount { get; set; }

        public DateTimeOffset? FirstFailureAt { get; set; }

        public DateTimeOffset LockedUntil { get; set; }
    }
}
