using Livia.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TemplateProject.EntityFrameworkCore;

/// <summary>
/// EF Core 设计时工厂，供 <c>dotnet ef</c> 命令使用。
/// 它从 Web 项目的 appsettings 中读取连接串与数据库提供程序，保证与运行时一致。
/// </summary>
/// <remarks>
/// 迁移属于各业务项目，因此模板<strong>不预置迁移</strong>，首次使用请执行：
/// <code>dotnet ef migrations add InitialCreate --project src/TemplateProject.EntityFrameworkCore --startup-project src/TemplateProject.Web</code>
/// 本工厂会依次尝试多个候选路径来定位 Web 项目，因此<strong>不依赖当前工作目录</strong>。
/// </remarks>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string WebProjectFolderName = "TemplateProject.Web";

    /// <inheritdoc />
    public AppDbContext CreateDbContext(string[] args)
    {
        var webProjectPath = ResolveWebProjectPath();

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

    /// <summary>
    /// 定位 Web 项目目录：依次尝试「从当前目录的同级目录」「从编译输出目录回推」「从当前目录下的 src」。
    /// </summary>
    private static string ResolveWebProjectPath()
    {
        string[] candidates =
        [
            // 在 EF 项目目录下执行 dotnet ef 时
            Path.Combine(Directory.GetCurrentDirectory(), "..", WebProjectFolderName),

            // 在仓库根目录执行 dotnet ef 时：<repo>/src/<Name>.EntityFrameworkCore/bin/<cfg>/<tfm> -> <repo>/src/<Name>.Web
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", WebProjectFolderName),

            // 在仓库根目录执行时的另一种布局：<repo>/src/<Name>.Web
            Path.Combine(Directory.GetCurrentDirectory(), "src", WebProjectFolderName)
        ];

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);

            if (Directory.Exists(fullPath) && File.Exists(Path.Combine(fullPath, "appsettings.json")))
            {
                return fullPath;
            }
        }

        throw new InvalidOperationException(
            $"未能定位 Web 项目目录（期望包含 appsettings.json 的 {WebProjectFolderName}）。已尝试：" +
            string.Join("；", candidates.Select(Path.GetFullPath)));
    }
}
