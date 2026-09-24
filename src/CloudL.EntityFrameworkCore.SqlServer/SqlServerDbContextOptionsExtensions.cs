using Microsoft.EntityFrameworkCore;

namespace CloudL.EntityFrameworkCore.Extensions;

/// <summary>
/// SQL Server 数据库配置。
/// </summary>
public static class SqlServerDbContextOptionsExtensions
{
    /// <summary>
    /// 使用 SQL Server 作为数据库提供程序。
    /// </summary>
    /// <remarks>
    /// 需要引用 <c>CloudL.EntityFrameworkCore.SqlServer</c> 包。
    /// 换 PostgreSQL 只需把包换成 <c>CloudL.EntityFrameworkCore.PostgreSql</c>，
    /// 并把这里改成 <c>UseCloudLPostgreSql</c> —— 框架核心包不需要任何改动。
    /// </remarks>
    public static DbContextOptionsBuilder UseCloudLSqlServer(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return optionsBuilder.UseSqlServer(connectionString);
    }
}
