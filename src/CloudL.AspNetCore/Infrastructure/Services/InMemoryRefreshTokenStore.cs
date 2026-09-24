using System.Collections.Concurrent;
using CloudL.Application.Contracts.IServices;
using Microsoft.Extensions.Logging;

namespace CloudL.AspNetCore.Infrastructure.Services;

/// <summary>
/// 进程内 Refresh Token 存储（默认实现）。
/// <para>适用于单实例部署与开发环境；<strong>多实例部署必须替换</strong>为共享存储（Redis / 数据库），
/// 否则负载均衡到另一实例时会判定 Token 无效。只需注册自己的 <see cref="IRefreshTokenStore"/> 即可覆盖。</para>
/// <para>Token 一次性消费（旋转），并在写入时顺带清理已过期条目，避免无界增长。</para>
/// </summary>
public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, TokenEntry> _tokens = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InMemoryRefreshTokenStore> _logger;

    public InMemoryRefreshTokenStore(
        TimeProvider timeProvider,
        ILogger<InMemoryRefreshTokenStore> logger)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StoreAsync(
        string refreshToken,
        Guid userId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        _tokens[refreshToken] = new TokenEntry(userId, expiresAt);
        SweepExpired();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Guid?> ConsumeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Task.FromResult<Guid?>(null);

        if (!_tokens.TryRemove(refreshToken, out var entry))
            return Task.FromResult<Guid?>(null);

        return Task.FromResult(entry.ExpiresAt > _timeProvider.GetUtcNow()
            ? entry.UserId
            : (Guid?)null);
    }

    /// <inheritdoc />
    public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            _tokens.TryRemove(refreshToken, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var revoked = 0;
        foreach (var pair in _tokens.Where(pair => pair.Value.UserId == userId).ToArray())
        {
            if (_tokens.TryRemove(pair.Key, out _))
                revoked++;
        }

        if (revoked > 0)
        {
            _logger.LogInformation("已撤销用户 {UserId} 的 {Count} 个 Refresh Token", userId, revoked);
        }

        return Task.CompletedTask;
    }

    private void SweepExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var pair in _tokens.Where(pair => pair.Value.ExpiresAt <= now).ToArray())
        {
            _tokens.TryRemove(pair.Key, out _);
        }
    }

    private readonly record struct TokenEntry(Guid UserId, DateTimeOffset ExpiresAt);
}
