using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using CloudL.AspNetCore.Configuration;
using CloudL.AspNetCore.HttpApi.Binding;
using CloudL.AspNetCore.HttpApi.Extensions;
using CloudL.AspNetCore.HttpApi.Filters;
using CloudL.AspNetCore.HttpApi.Swagger;
using CloudL.AspNetCore.Infrastructure.Services;
using CloudL.AspNetCore.Json;
using CloudL.AspNetCore.Logging;
using CloudL.Application.Contracts.IServices;
using CloudL.Domain.Shared.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CloudL.AspNetCore.Extensions;

/// <summary>
/// CloudL.AspNetCore 的依赖注入入口。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 ASP.NET Core 集成：配置校验、当前用户、密码哈希、Refresh Token、JWT 认证、CORS 与 HTTP 客户端。
    /// </summary>
    /// <remarks>
    /// 配置项缺失或非法会在<strong>应用启动阶段</strong>直接抛出异常（ValidateOnStart），
    /// 而不是等到第一次请求才以 500 的形式暴露。
    /// </remarks>
    public static IServiceCollection AddCloudLAspNetCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PasswordHasherOptions>()
            .Bind(configuration.GetSection(PasswordHasherOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();

        services.TryAddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.TryAddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
        services.TryAddScoped<ICurrentUser, CurrentUser>();
        services.TryAddScoped<IJwtTokenService, JwtTokenService>();
        services.TryAddScoped<IHttpClientService, HttpClientService>();

        AddCloudLHttpClients(services, configuration);
        services.AddCloudLJwtAuthentication();
        services.AddCloudLCors(configuration);
        services.AddCloudLRateLimiting(configuration);

        // query 参数同样采用 snake_case（与 JSON 请求/响应体、验证错误键保持一致）；
        // 同时保留 camelCase 写法，避免破坏既有调用方。
        // query 参数：键名统一走 snake_case；含大写字母的参数名由 QueryParameterNamingFilter 直接拒绝
        services.Configure<MvcOptions>(options =>
        {
            options.ValueProviderFactories.Insert(0, new SnakeCaseQueryValueProviderFactory());
            options.Filters.Add<QueryParameterNamingFilter>();
        });

        return services;
    }

    /// <summary>
    /// 让 MVC 管道使用框架统一的 JSON 约定（snake_case、忽略 null、不转义中文），
    /// 与中间件、JWT 事件写出的响应体保持一致。
    /// </summary>
    public static IMvcBuilder AddCloudLJsonOptions(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddJsonOptions(options =>
        {
            var source = CloudLJson.SerializerOptions;
            var target = options.JsonSerializerOptions;
            target.PropertyNamingPolicy = source.PropertyNamingPolicy;
            target.DefaultIgnoreCondition = source.DefaultIgnoreCondition;
            target.Encoder = source.Encoder;
            target.PropertyNameCaseInsensitive = true;
        });

        return builder;
    }

    /// <summary>
    /// 注册 JWT Bearer 认证，并让 401 / 403 也返回统一的 <see cref="ApiResponse"/>。
    /// </summary>
    public static IServiceCollection AddCloudLJwtAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptionsAccessor) =>
            {
                var jwt = jwtOptionsAccessor.Value;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
                    ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds)
                };

                bearer.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // SignalR：允许通过 query string 传递 access_token
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken)
                            && context.Request.Path.StartsWithSegments("/signalr"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },

                    OnChallenge = async context =>
                    {
                        // 阻止默认的 401 纯文本响应
                        context.HandleResponse();
                        await WriteAuthFailureAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            ErrorCodes.Unauthorized,
                            ResolveChallengeMessage(context)).ConfigureAwait(false);
                    },

                    OnForbidden = async context =>
                    {
                        await WriteAuthFailureAsync(
                            context.HttpContext,
                            StatusCodes.Status403Forbidden,
                            ErrorCodes.Forbidden,
                            "权限不足，拒绝访问").ConfigureAwait(false);
                    }
                };
            });

        return services;
    }

    /// <summary>
    /// 注册 CORS 策略。白名单优先；<c>Cors:AllowAnyOrigin</c> 默认为 false，未配置白名单时不放行任何来源。
    /// </summary>
    public static IServiceCollection AddCloudLCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var cors = new CorsOptions();
        configuration.GetSection(CorsOptions.SectionName).Bind(cors);

        services.AddCors(options =>
        {
            options.AddPolicy(CorsOptions.DefaultPolicyName, policy =>
            {
                if (cors.AllowedOrigins.Length > 0)
                {
                    policy.WithOrigins(cors.AllowedOrigins);

                    if (cors.AllowCredentials)
                    {
                        policy.AllowCredentials();
                    }
                }
                else if (cors.AllowAnyOrigin)
                {
                    policy.AllowAnyOrigin();
                }
                else
                {
                    // 安全默认：未配置白名单且未显式放开时，拒绝所有跨域来源
                    policy.SetIsOriginAllowed(_ => false);
                }

                policy.AllowAnyMethod().AllowAnyHeader();

                if (cors.ExposedHeaders.Length > 0)
                {
                    policy.WithExposedHeaders(cors.ExposedHeaders);
                }
            });
        });

        return services;
    }

    /// <summary>
    /// 注册 Swagger / OpenAPI，并内置 Bearer 安全定义。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="title">文档标题。</param>
    /// <param name="xmlDocumentAssemblies">需要加载 XML 注释的程序集（控制器、DTO 所在程序集）。</param>
    public static IServiceCollection AddCloudLSwagger(
        this IServiceCollection services,
        string title = "CloudL API",
        params Assembly[] xmlDocumentAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = title,
                Version = "v1",
                Description = "由 CloudL DDD 框架生成的 API 文档"
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "请输入 JWT（无需添加 Bearer 前缀）",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            // query 参数在文档里也显示为 snake_case，保证 Swagger UI 生成的请求可直接使用
            options.OperationFilter<SnakeCaseQueryParameterOperationFilter>();

            foreach (var assembly in (xmlDocumentAssemblies ?? []).Where(a => a is not null).Distinct())
            {
                IncludeXmlCommentsIfExists(options, assembly);
            }
        });

        return services;
    }

    /// <summary>
    /// 启用 Swagger 中间件与 UI（通常仅在开发环境调用）。
    /// </summary>
    public static WebApplication UseCloudLSwagger(
        this WebApplication app,
        string routePrefix = "swagger")
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
            options.RoutePrefix = routePrefix;
        });

        return app;
    }

    private static void AddCloudLHttpClients(IServiceCollection services, IConfiguration configuration)
    {
        var sections = configuration.GetSection(HttpClientEntryOptions.SectionName).GetChildren().ToList();

        if (sections.All(section =>
                !string.Equals(section.Key, IHttpClientService.DefaultClientName, StringComparison.Ordinal)))
        {
            // 配置中未定义 Default 客户端时补一个，保证默认调用（省略 clientName）可用
            services.AddHttpClient(IHttpClientService.DefaultClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });
        }

        foreach (var section in sections)
        {
            var clientName = section.Key;
            services.Configure<HttpClientEntryOptions>(clientName, section);

            services.AddHttpClient(clientName, (serviceProvider, client) =>
            {
                var entry = serviceProvider
                    .GetRequiredService<IOptionsMonitor<HttpClientEntryOptions>>()
                    .Get(clientName);

                if (!string.IsNullOrWhiteSpace(entry.BaseUrl)
                    && Uri.TryCreate(entry.BaseUrl, UriKind.Absolute, out var baseAddress))
                {
                    client.BaseAddress = baseAddress;
                }

                client.Timeout = TimeSpan.FromSeconds(entry.TimeoutSeconds);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });
        }
    }

    private static async Task WriteAuthFailureAsync(
        HttpContext httpContext,
        int statusCode,
        int businessCode,
        string message)
    {
        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("CloudL.JwtBearer");

        var authHeader = SensitiveDataRedactor.RedactAuthorizationHeader(
            httpContext.Request.Headers.Authorization.ToString());

        logger.LogWarning(
            "\n认证/授权失败: {Message}\n{Method} {Path}{QueryString}\n[Auth]\n{AuthHeader}",
            message,
            httpContext.Request.Method,
            httpContext.Request.Path,
            httpContext.Request.QueryString,
            authHeader);

        if (httpContext.Response.HasStarted)
            return;

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response
            .WriteAsync(CloudLJson.Serialize(ApiResponse.Fail(businessCode, message)))
            .ConfigureAwait(false);
    }

    private static string ResolveChallengeMessage(JwtBearerChallengeContext context)
    {
        if (context.AuthenticateFailure is SecurityTokenExpiredException expired)
        {
            return $"token 已过期（过期时间 {expired.Expires:yyyy-MM-dd HH:mm:ss} UTC）";
        }

        return string.IsNullOrEmpty(context.ErrorDescription)
            ? "未授权访问，请提供有效的认证凭据"
            : context.ErrorDescription;
    }

    private static void IncludeXmlCommentsIfExists(SwaggerGenOptions options, Assembly assembly)
    {
        var fileName = $"{assembly.GetName().Name}.xml";

        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory, fileName)
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                options.IncludeXmlComments(candidate);
                return;
            }
        }
    }
}
