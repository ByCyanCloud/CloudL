namespace CloudL.Application.Contracts.IServices;

/// <summary>
/// 登录失败计数与临时锁定（<strong>账号维度</strong>）。
/// <para>与限流中间件的 <strong>IP 维度</strong>互补：中间件阶段用户名还在请求体里（拿不到），
/// 所以账号维度只能在登录流程内部完成。两个维度必须互相独立 ——
/// 把 IP 与用户名拼成一个分区键反而更弱，攻击者换个用户名就能拿到新配额。</para>
/// </summary>
public interface ILoginAttemptGuard
{
    /// <summary>
    /// 账号是否处于锁定期；处于锁定期则抛出 <see cref="Domain.Shared.Exceptions.TooManyRequestsException"/>
    /// （HTTP 429，业务码 <c>4291</c>）。
    /// </summary>
    void EnsureNotLocked(string userName);

    /// <summary>记录一次登录失败；返回本次是否<strong>触发</strong>了锁定。</summary>
    bool RecordFailure(string userName);

    /// <summary>登录成功：清除失败计数与锁定状态。</summary>
    void Reset(string userName);
}
