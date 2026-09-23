using Livia.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TemplateProject.Domain.Entities;
using TemplateProject.Domain.Shared.Constants;
using TemplateProject.EntityFrameworkCore;

// ============================================================
// TemplateProject DbMigrator — 数据库迁移与种子数据
// 用法: dotnet run --project src/TemplateProject.DbMigrator
// ============================================================

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("====== DbMigrator 启动 ======");

    using var host = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((context, config) => config
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
            .AddEnvironmentVariables())
        .UseSerilog((context, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .WriteTo.Console())
        .ConfigureServices((context, services) =>
        {
            services.AddProjectEntityFrameworkCore<AppDbContext>(context.Configuration);
        })
        .Build();

    await using var scope = host.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var provider = host.Services.GetRequiredService<IConfiguration>()
        .GetValue<string>("Database:Provider") ?? "postgresql";
    Log.Information("数据库提供程序: {Provider}", provider);

    // ---------- 迁移 ----------
    var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync()).ToList();

    if (pendingMigrations.Count == 0)
    {
        Log.Information("数据库已是最新，无需迁移");
    }
    else
    {
        Log.Information("发现 {Count} 个待应用的迁移:", pendingMigrations.Count);
        foreach (var migration in pendingMigrations)
        {
            Log.Information("  - {Migration}", migration);
        }

        await dbContext.Database.MigrateAsync();
        Log.Information("数据库迁移完成");
    }

    // ---------- 种子数据：仅插入缺失的角色 ----------
    var existingRoleCodes = await dbContext.Roles
        .Select(role => role.Code)
        .ToListAsync();

    var roleSeeds = new (string Code, string Name)[]
    {
        (RoleCode.User, "普通用户"),
        (RoleCode.Manager, "管理者"),
        (RoleCode.Admin, "管理员"),
        (RoleCode.Super, "超级管理员")
    };

    var missingRoles = roleSeeds
        .Where(seed => !existingRoleCodes.Contains(seed.Code))
        .Select(seed => new Role(seed.Code, seed.Name))
        .ToList();

    if (missingRoles.Count > 0)
    {
        await dbContext.Roles.AddRangeAsync(missingRoles);
        await dbContext.SaveChangesAsync();
        Log.Information("已补充 {Count} 个角色: {Roles}",
            missingRoles.Count,
            string.Join(", ", missingRoles.Select(role => role.Code)));
    }
    else
    {
        Log.Information("角色种子数据已存在，跳过");
    }

    Log.Information("====== DbMigrator 完成 ======");
}
catch (Exception exception)
{
    Log.Fatal(exception, "DbMigrator 执行失败");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
