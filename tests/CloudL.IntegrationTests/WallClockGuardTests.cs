using System;
using System.Linq;
using System.Threading.Tasks;
using CloudL.Domain.Entities;
using CloudL.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CloudL.IntegrationTests;

/// <summary>
/// A-2 墙上钟守卫：经过一次真实 <c>SaveChanges</c> 后，写库的 <see cref="DateTime"/> 一律是
/// <see cref="DateTimeKind.Unspecified"/>（墙上钟），即使传进来的是 <c>Kind=Utc</c>。
/// </summary>
/// <remarks>
/// 刻意<strong>不改全局时钟口径</strong>（保持默认 <c>Utc</c>）：这样断言的是"Kind 被归一"这个核心效果，
/// 既确定又不影响并行运行的其它测试。默认口径下 Utc → 墙上钟的<strong>数值不变</strong>、只有 Kind 变，
/// 因此断言"值相等 + Kind=Unspecified"正好精确锁定守卫的作用。
/// </remarks>
public class WallClockGuardTests
{
    [Fact]
    public async Task SaveChanges_WithUtcKind_ShouldPersistUnspecifiedKind()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<WallClockGuardDbContext>()
            .UseSqlite(connection)
            .Options;

        var utcMoment = new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

        await using (var context = new WallClockGuardDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();

            context.Items.Add(new WallClockItem { Moment = utcMoment, OptionalMoment = utcMoment });
            await context.SaveChangesAsync();
        }

        await using (var context = new WallClockGuardDbContext(options))
        {
            var saved = await context.Items.AsNoTracking().SingleAsync();

            Assert.Equal(DateTimeKind.Unspecified, saved.Moment.Kind);
            Assert.Equal(new DateTime(2026, 9, 25, 10, 0, 0), saved.Moment);

            Assert.NotNull(saved.OptionalMoment);
            Assert.Equal(DateTimeKind.Unspecified, saved.OptionalMoment!.Value.Kind);
            Assert.Equal(new DateTime(2026, 9, 25, 10, 0, 0), saved.OptionalMoment.Value);
        }
    }

    [Fact]
    public async Task SaveChanges_WithUnspecifiedKind_ShouldKeepValueUntouched()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<WallClockGuardDbContext>()
            .UseSqlite(connection)
            .Options;

        var wallClock = new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Unspecified);

        await using (var context = new WallClockGuardDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();

            context.Items.Add(new WallClockItem { Moment = wallClock });
            await context.SaveChangesAsync();
        }

        await using (var context = new WallClockGuardDbContext(options))
        {
            var saved = await context.Items.AsNoTracking().SingleAsync();

            Assert.Equal(wallClock, saved.Moment);
            Assert.Equal(DateTimeKind.Unspecified, saved.Moment.Kind);
        }
    }
}

/// <summary>测试实体：同时覆盖 <see cref="DateTime"/> 与 <see cref="Nullable{DateTime}"/> 两种属性。</summary>
public sealed class WallClockItem : Entity<Guid>
{
    public DateTime Moment { get; set; }

    public DateTime? OptionalMoment { get; set; }
}

/// <summary>最小可用的派生上下文（走框架的 <c>FrameworkDbContext</c>，从而经过 A-2 的守卫）。</summary>
public sealed class WallClockGuardDbContext : FrameworkDbContext
{
    public WallClockGuardDbContext(DbContextOptions<WallClockGuardDbContext> options)
        : base(options)
    {
    }

    public DbSet<WallClockItem> Items => Set<WallClockItem>();
}
