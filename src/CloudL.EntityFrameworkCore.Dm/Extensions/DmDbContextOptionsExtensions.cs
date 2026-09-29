using Microsoft.EntityFrameworkCore;

namespace CloudL.EntityFrameworkCore.Extensions;

/// <summary>
/// 达梦（DM）数据库配置。
/// </summary>
/// <remarks>
/// <para>需要引用 <c>CloudL.EntityFrameworkCore.Dm</c> 包。用法与其它提供程序完全一致：</para>
/// <code>
/// services.AddCloudLEntityFrameworkCore&lt;AppDbContext&gt;(options =&gt;
///     options.UseCloudLDm(configuration.GetRequiredConnectionString()));
/// </code>
/// <para>⚠️ <strong>版本错位提醒</strong>：达梦官方 EF Core 提供程序目前最新为 9.0.0.x（面向 EF Core 9），
/// 而框架本体使用 EF Core 10。版本区间能够还原，但 provider 与 EF 强绑定，
/// 属于<strong>未经真实达梦实例验证</strong>的组合 —— 请先在测试库确认增删改查与迁移，
/// 官方发布 EF Core 10 版后立即升级本包依赖。</para>
/// <para>时间列约定：框架不存储时区，达梦侧使用不带时区的 <c>TIMESTAMP</c>
/// （见 <c>FrameworkDbContext.ConfigureConventions</c>）。</para>
/// </remarks>
public static class DmDbContextOptionsExtensions
{
    /// <summary>
    /// 使用达梦（DM）作为数据库提供程序。
    /// </summary>
    public static DbContextOptionsBuilder UseCloudLDm(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return optionsBuilder.UseDm(connectionString);
    }
}
