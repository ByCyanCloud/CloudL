using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TemplateProject.Infrastructure;

/// <summary>
/// Infrastructure 层依赖注入注册。
/// </summary>
/// <remarks>
/// 本层只放<strong>业务侧</strong>的基础设施实现（邮件、短信、文件存储、第三方 API 适配器等）。
/// 框架级基础设施 —— 密码哈希、JWT 令牌、当前用户上下文、Refresh Token 存储、HTTP 客户端 ——
/// 已由 <c>CloudL.AspNetCore</c> 提供，无需在此重复注册。
/// </remarks>
public static class ProjectInfrastructureModule
{
    /// <summary>
    /// 注册业务侧基础设施实现。
    /// </summary>
    public static IServiceCollection AddProjectInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 示例：
        // services.AddScoped<IEmailSender, SmtpEmailSender>();
        // services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));
        // services.AddScoped<IPaymentGateway, WeChatPaymentGateway>();

        return services;
    }
}
