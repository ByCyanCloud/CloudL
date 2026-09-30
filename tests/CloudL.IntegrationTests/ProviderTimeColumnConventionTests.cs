using System;
using CloudL.Domain.Entities;
using CloudL.EntityFrameworkCore;
using CloudL.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// 回归测试：框架"时间列一律不带时区"的约定，必须对 Npgsql 与达梦（DM）<strong>两个提供程序都生效</strong>。
/// </summary>
/// <remarks>
/// <para>
/// 背景（这是一次真实 bug 的钉子）：<c>FrameworkDbContext.ConfigureConventions</c> 曾经把达梦分支
/// 写在 <c>Database.ProviderName.Contains("Npgsql")</c> 的 <c>if</c> <strong>内部</strong>。
/// 达梦的 ProviderName 是 <c>DM.Microsoft.EntityFrameworkCore</c>，不含 "Npgsql"，
/// 于是外层条件恒为 false —— 达梦那段约定<strong>一次也不会执行</strong>（静默失效、不报错），
/// 时间列类型悄悄退化成 provider 默认值。
/// </para>
/// <para>
/// 两个断言缺一不可：只断言达梦，会放过"把 Npgsql 那一支删掉"；只断言 Npgsql，
/// 会放过原来那种嵌套写法。两条一起才能锁住"两个提供程序各自生效"。
/// </para>
/// <para>
/// 断言本身是有效的：Npgsql 对 <see cref="DateTime"/> 的默认映射是 <c>timestamp with time zone</c>，
/// 而这里用的是 Sqlite 模型栈（默认 <c>TEXT</c>）—— 拿到期望值只可能来自框架那段显式约定。
/// </para>
/// <para>
/// 达梦侧刻意<strong>不调用 <c>UseCloudLDm</c></strong>：达梦官方 EF Core 9 版提供程序
/// （DM.Microsoft.EntityFrameworkCore 9.0.0.x）在 EF Core 10 上<strong>调用即抛
/// <see cref="MissingMethodException"/></strong>（包描述里那条"版本错位"提醒的实际表现），
/// 根本走不到模型构建，因此这里改用"真的 provider 名 + 假的提供程序描述符"来触发同一个分支，
/// 全程<strong>不连库</strong>。
/// </para>
/// <para>
/// 另外，EF Core 的模型按<strong>上下文类型</strong>缓存，同一个上下文类型不能用于两个提供程序，
/// 因此这里用两个派生上下文，各构建一次模型。
/// </para>
/// </remarks>
public class ProviderTimeColumnConventionTests
{
    /// <summary>达梦官方 EF Core 提供程序的真实 <c>Database.ProviderName</c>。</summary>
    private const string DmProviderName = "DM.Microsoft.EntityFrameworkCore";

    [Fact]
    public void DmProvider_DateTimeColumns_ShouldUseTimestamp()
    {
        var optionsBuilder = new DbContextOptionsBuilder<DmTimeColumnDbContext>();
        optionsBuilder.UseSqlite("DataSource=:memory:");
        optionsBuilder.ReplaceService<IDatabaseProvider, FakeDmDatabaseProvider>();

        using var context = new DmTimeColumnDbContext(optionsBuilder.Options);

        Assert.Equal(DmProviderName, context.Database.ProviderName);
        Assert.Equal("TIMESTAMP", ColumnType(context, nameof(TimeColumnItem.Moment)));
        Assert.Equal("TIMESTAMP", ColumnType(context, nameof(TimeColumnItem.OptionalMoment)));
    }

    [Fact]
    public void NpgsqlProvider_DateTimeColumns_ShouldUseTimestampWithoutTimeZone()
    {
        // 这一侧用真实提供程序（Npgsql），同样不连库：只构建模型。
        var optionsBuilder = new DbContextOptionsBuilder<NpgsqlTimeColumnDbContext>();
        optionsBuilder.UseCloudLPostgreSql("Host=model-only;Port=5432;Database=model-only;Username=model-only;Password=model-only");

        using var context = new NpgsqlTimeColumnDbContext(optionsBuilder.Options);

        Assert.Equal("timestamp without time zone", ColumnType(context, nameof(TimeColumnItem.Moment)));
        Assert.Equal("timestamp without time zone", ColumnType(context, nameof(TimeColumnItem.OptionalMoment)));
    }

    private static string? ColumnType(DbContext context, string propertyName)
    {
        var entityType = context.Model.FindEntityType(typeof(TimeColumnItem));
        Assert.NotNull(entityType);

        var property = entityType!.FindProperty(propertyName);
        Assert.NotNull(property);

        return property!.GetColumnType();
    }
}

/// <summary>
/// 只报告 <c>Database.ProviderName</c> 的提供程序描述符：模型仍由真实提供程序（这里用 Sqlite）构建，
/// 但框架看到的名字是达梦的 —— 正好精确复现"分支靠名字匹配"这个前提。
/// </summary>
public sealed class FakeDmDatabaseProvider : IDatabaseProvider
{
    public string Name => "DM.Microsoft.EntityFrameworkCore";

    public string Version => "9.0.0";

    public bool IsConfigured(IDbContextOptions options) => true;
}

/// <summary>测试实体：同时覆盖 <see cref="DateTime"/> 与 <see cref="Nullable{DateTime}"/> 两种属性。</summary>
public sealed class TimeColumnItem : Entity<Guid>
{
    public DateTime Moment { get; set; }

    public DateTime? OptionalMoment { get; set; }
}

/// <summary>达梦（DM）侧的最小派生上下文。</summary>
public sealed class DmTimeColumnDbContext : FrameworkDbContext
{
    public DmTimeColumnDbContext(DbContextOptions<DmTimeColumnDbContext> options)
        : base(options)
    {
    }

    public DbSet<TimeColumnItem> Items => Set<TimeColumnItem>();
}

/// <summary>PostgreSQL（Npgsql）侧的最小派生上下文。</summary>
public sealed class NpgsqlTimeColumnDbContext : FrameworkDbContext
{
    public NpgsqlTimeColumnDbContext(DbContextOptions<NpgsqlTimeColumnDbContext> options)
        : base(options)
    {
    }

    public DbSet<TimeColumnItem> Items => Set<TimeColumnItem>();
}
