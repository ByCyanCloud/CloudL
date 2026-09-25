// ============================================================================
// 本文件由 CloudL 模板生成，属于「框架装配」部分。
// 升级 CloudL.* 包不会更新本文件（模板是复制，不是依赖）。
// 需要同步模板改进时，在框架仓库执行：
//     pwsh ./build/compare-template.ps1 -ProjectPath <你的项目根目录>
// ============================================================================
using CloudL.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TemplateProject.EntityFrameworkCore;

/// <summary>
/// EF Core 设计时工厂，供 <c>dotnet ef</c> 命令使用。
/// 它从 Web 项目的 appsettings 中读取连接串，并复用与运行时相同的提供程序配置，
/// 保证迁移生成的结果与实际运行一致。
/// </summary>
/// <remarks>
/// 迁移属于各业务项目，因此模板<strong>不预置迁移</strong>，首次使用请执行：
/// <code>dotnet ef migrations add InitialCreate --project src/TemplateProject.EntityFrameworkCore --startup-project src/TemplateProject.Web</code>
/// 本工厂会依次尝试多个候选路径来定位 Web 项目，因此<strong>不依赖当前工作目录</strong>。
/// 换数据库时，请把下面的 <c>UseCloudLPostgreSql</c> 与项目引用一起改掉（与运行时保持一致）。
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

        var connectionString = configuration.GetRequiredConnectionString();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseCloudLPostgreSql(connectionString);

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
