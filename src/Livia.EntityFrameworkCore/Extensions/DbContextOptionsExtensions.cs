using Microsoft.EntityFrameworkCore;

namespace Livia.EntityFrameworkCore.Extensions;

/// <summary>
/// 数据库提供程序配置。
/// </summary>
public static class DbContextOptionsExtensions
{
    /// <summary>SQL Server 提供程序名称。</summary>
    public const string SqlServerProvider = "sqlserver";

    /// <summary>PostgreSQL 提供程序名称。</summary>
    public const string PostgreSqlProvider = "postgresql";

    /// <summary>
    /// 按提供程序名称配置数据库连接。
    /// 未知的提供程序会<strong>立即抛出异常</strong>，而不是静默回退到某个提供程序。
    /// </summary>
    public static DbContextOptionsBuilder UseLiviaDatabaseProvider(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        string provider)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        return provider.Trim().ToLowerInvariant() switch
        {
            PostgreSqlProvider => optionsBuilder.UseNpgsql(connectionString),
            SqlServerProvider => optionsBuilder.UseSqlServer(connectionString),
            _ => throw new NotSupportedException(
                $"不支持的数据库提供程序 “{provider}”。可选值：{SqlServerProvider}、{PostgreSqlProvider}。")
        };
    }
}