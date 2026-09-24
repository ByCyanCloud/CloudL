using Microsoft.Extensions.Configuration;

namespace CloudL.EntityFrameworkCore.Extensions;

/// <summary>
/// 配置读取辅助。
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// 读取必需的数据库连接串（默认 <c>ConnectionStrings:Default</c>）。
    /// </summary>
    /// <remarks>
    /// 缺失或为空白时<strong>立即抛出异常并指出配置路径</strong>，
    /// 而不是等到第一次访问数据库时以晦涩的错误暴露。
    /// </remarks>
    public static string GetRequiredConnectionString(this IConfiguration configuration, string name = "Default")
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(name);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"未配置数据库连接串：ConnectionStrings:{name}。请在 appsettings、用户机密或环境变量中配置。");
        }

        return connectionString;
    }
}
