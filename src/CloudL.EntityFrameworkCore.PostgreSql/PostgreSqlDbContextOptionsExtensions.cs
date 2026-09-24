using Microsoft.EntityFrameworkCore;

namespace CloudL.EntityFrameworkCore.Extensions;

/// <summary>
/// PostgreSQL（Npgsql）数据库配置。
/// </summary>
public static class PostgreSqlDbContextOptionsExtensions
{
    /// <summary>
    /// 使用 PostgreSQL 作为数据库提供程序。
    /// </summary>
    /// <remarks>
    /// 需要引用 <c>CloudL.EntityFrameworkCore.PostgreSql</c> 包。
    /// 换 SQL Server 只需把包换成 <c>CloudL.EntityFrameworkCore.SqlServer</c>，
    /// 并把这里改成 <c>UseCloudLSqlServer</c> —— 框架核心包不需要任何改动。
    /// </remarks>
    public static DbContextOptionsBuilder UseCloudLPostgreSql(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return optionsBuilder.UseNpgsql(connectionString);
    }
}
