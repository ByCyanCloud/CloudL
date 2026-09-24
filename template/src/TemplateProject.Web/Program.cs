using CloudL.Application;
using CloudL.AspNetCore.Configuration;
using CloudL.AspNetCore.Extensions;
using CloudL.AspNetCore.HttpApi.Filters;
using CloudL.AspNetCore.HttpApi.Middlewares;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using TemplateProject.Application;
using TemplateProject.Application.Contracts.Dtos;
using TemplateProject.Application.Mappings;
using TemplateProject.Domain.Shared.Constants;
using TemplateProject.EntityFrameworkCore;
using TemplateProject.HttpApi.Controllers;
using TemplateProject.Infrastructure;

// ============================================================
// TemplateProject — 基于 CloudL 框架的 DDD Web API
// ============================================================

var builder = WebApplication.CreateBuilder(args);

// ---------- 日志：Serilog（配置来自 appsettings.json） ----------
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// ---------- 框架：领域事件分发 + Mapster 全局约定 + 扫描业务映射 ----------
builder.Services.AddCloudLCore(typeof(UserMappingRegister).Assembly);

// ---------- 框架：JWT / CORS / Swagger / 当前用户 / 密码哈希 / HTTP 客户端 ----------
// 配置缺失或非法会在启动阶段直接报错（ValidateOnStart），不会拖到第一次请求
builder.Services.AddCloudLAspNetCore(builder.Configuration);

// ---------- 业务各层 ----------
builder.Services.AddProjectInfrastructure(builder.Configuration);
builder.Services.AddProjectApplication();
builder.Services.AddProjectEntityFrameworkCore<AppDbContext>(builder.Configuration);

// ---------- MVC：统一 JSON 约定 + 模型验证过滤器 ----------
// 关闭默认的 ModelState 自动 400，改由框架的 ValidationFilter 输出统一响应体
builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.SuppressModelStateInvalidFilter = true);

builder.Services
    .AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddCloudLJsonOptions();

// ---------- 授权 ----------
builder.Services.AddAuthorization(options =>
{
    // 默认要求已认证：新增接口默认受保护，匿名访问必须显式 [AllowAnonymous]
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy("AdminOrAbove", policy =>
        policy.RequireRole(RoleCode.Admin, RoleCode.Super));

    options.AddPolicy("ManagerOrAbove", policy =>
        policy.RequireRole(RoleCode.Manager, RoleCode.Admin, RoleCode.Super));
});

// ---------- 健康检查（含数据库连通性） ----------
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

// ---------- Swagger（含 JWT 安全定义与 XML 注释） ----------
builder.Services.AddCloudLSwagger(
    "TemplateProject API",
    typeof(UsersController).Assembly,
    typeof(UserDto).Assembly);

// ============================================================
var app = builder.Build();

// 启用请求体缓冲，使过滤器/中间件可以多次读取请求体（日志与诊断需要）
app.Use(async (context, next) =>
{
    context.Request.EnableBuffering();
    await next(context);
});

// 请求日志：每个请求一条摘要（query 中的敏感参数自动脱敏）；/health、/swagger 降级为 Verbose
app.UseCloudLRequestLogging();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseCloudLSwagger();
}

app.UseCors(CorsOptions.DefaultPolicyName);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

try
{
    Log.Information("正在启动 {Application} ...", app.Environment.ApplicationName);
    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "应用启动失败");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
