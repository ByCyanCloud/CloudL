using Livia.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TemplateProject.EntityFrameworkCore;

/// <summary>
/// EF Core 设计时工厂，供 <c>dotnet ef</c> 命令使用。
/// 它从 Web 项目的 appsettings 中读取连接串与数据库提供程序，保证与运行时一致。
/// 如需支持环境变量（例如 CI 中覆盖连接串），为项目加上
/// <c>Microsoft.Extensions.Configuration.EnvironmentVariables</c> 包后调用 AddEnvironmentVariables 即可。
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <inheritdoc />
    public AppDbContext CreateDbContext(string[] args)
    {
        var webProjectPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "TemplateProject.Web");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(webProjectPath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("未配置数据库连接串：ConnectionStrings:Default");

        var provider = configuration.GetValue<string>("Database:Provider")
            ?? DbContextOptionsExtensions.PostgreSqlProvider;

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseLiviaDatabaseProvider(connectionString, provider);

        return new AppDbContext(optionsBuilder.Options);
    }
}
