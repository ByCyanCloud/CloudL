# CloudL

面向 **.NET 10** 的 DDD Web API 框架。框架以 **NuGet 包**形式发布，业务项目通过引用包使用框架；
框架升级 = 改版本号（或 `dotnet package update`），不再需要复制/同步源码。

## 包一览

| 包 | 内容 |
|---|---|
| `CloudL.Core` | 领域基类（`Entity<TKey>`/`AuditableEntity`/`ValueObject`）、共享内核（常量、异常）、应用契约（分页 DTO、服务接口）、领域事件分发、Mapster 全局约定 |
| `CloudL.EntityFrameworkCore` | `FrameworkDbContext`（审计/乐观锁/领域事件）、`EfCoreRepository<,>`、`EfCoreUnitOfWork`、实体配置基类、`AddFrameworkDbContext<TContext>()` |
| `CloudL.AspNetCore` | 统一响应 `ApiResponse`、全局异常中间件、模型验证过滤器、JWT/CORS/Swagger 集成、当前用户上下文、PBKDF2 密码哈希、HTTP 客户端封装、`AddFramework()` |

## 业务项目快速开始

```powershell
dotnet new cloudl -n MyCompany.BookStore -o ./BookStore
cd ./BookStore
dotnet build
```

生成的骨架保留九层结构，但只承载业务代码；框架代码全部来自上表三个包。

## 目录结构

```
CloudL/
├── Directory.Build.props          # 全仓库统一的 TFM / 语言 / 包元数据
├── Directory.Packages.props       # 中央包管理：所有版本集中在此
├── nuget.config                   # 包源 + 源映射（防依赖混淆）
├── CloudL.slnx                     # 框架解决方案
├── build/pack.ps1                 # 打包脚本：pack + 推本地源 + 装模板
├── src/                           # 三个框架包
├── tests/                         # 单元测试 + 模板冒烟测试
├── template/                      # dotnet new 模板（九层业务骨架）
└── local-feed/                    # 本机 NuGet 源（打包输出）
```

## 框架开发者指南

```powershell
# 构建
dotnet build CloudL.slnx

# 打包并推送到本机源 local-feed，然后重装模板
pwsh ./build/pack.ps1

# 冒烟验证：生成一个项目并编译
dotnet new cloudl -n SmokeTest -o ./artifacts/smoke
dotnet build ./artifacts/smoke
```

## 版本与发布

- 版本号由 **MinVer** 依据 git tag（`v1.2.3`）自动计算，三个包锁步同版本。
- 发布新版本：`git tag v0.2.0 && git push --tags`，再执行 `build/pack.ps1 -Push`。
- 每个版本的**迁移影响**（是否需要业务侧新增 EF 迁移、是否含破坏性变更）记录在 `CHANGELOG.md`。

## 升级业务项目

```powershell
# 升级到最新
dotnet package update
# 或锁定到指定版本
dotnet package update CloudL.Core@0.2.0 CloudL.EntityFrameworkCore@0.2.0 CloudL.AspNetCore@0.2.0
# 回滚
dotnet package update CloudL.Core@0.1.0
```

## API 命名约定

框架统一采用 **snake_case**，业务项目无需额外配置：

| 位置 | 约定 | 示例 |
|---|---|---|
| JSON 请求体 / 响应体 | snake_case | `{"user_name":"alice","row_version":"..."}` |
| Query 参数 | snake_case（唯一约定，含大写字母的参数名返回 400） | `?page_index=2&page_size=50&sort_by=user_name` |
| 验证错误键 | snake_case | `{"errors":{"page_index":["页码必须大于等于 1"]}}` |
| 业务字典的键 | **原样保留**（不做转换，避免 `USD` → `usd` 这类语义损坏） | `{"USD":"美元"}` |
| Swagger 文档 | 与上一致（query 参数同样显示为 snake_case） | — |

实现：`CloudLJson`（JSON）、`SnakeCaseQueryValueProviderFactory`（query 绑定）、
`SnakeCaseQueryParameterOperationFilter`（Swagger），由 `AddCloudLAspNetCore` 自动接入。

> 说明：`QueryParameterNamingFilter` 会校验 query 参数名 —— **含大写字母（camelCase / PascalCase）时直接返回 400**，
> 并在响应中列出不合法的参数名；键允许的字符为小写字母、数字、下划线、点（嵌套分隔）与连字符。
> 这样做是为了避免"参数名不匹配 → 静默使用默认值 → 调用方拿到错误结果却没有任何提示"。

## 日志约定

- **请求日志**：`app.UseCloudLRequestLogging()` —— 每个请求一条摘要（方法、路径、状态码、耗时、TraceId、客户端 IP、用户名）。
  query string 中的敏感参数（如 `access_token`）会自动遮蔽；`/health`、`/swagger`、`/favicon.ico` 降级为 Verbose。
- **异常与验证失败**：由 `ExceptionHandlingMiddleware` 与 `ValidationFilter` 输出，请求体与 Authorization 头经脱敏，
  块状日志以空行开头，便于在日志文件中区分不同日志段。
- **脱敏两层策略**：按键名（忽略大小写与 `_`/`-`，因此 `access_token`、`api-key`、`client_secret` 均命中）
  与按值的形态（JWT、≥40 位不透明令牌），键名起成 `ticket` 也遮得住。

## 限流

- **IP 维度**（已实现）：`AddCloudLRateLimiting` 由 `AddCloudLAspNetCore` 自动注册；
  管道里加 `app.UseRateLimiter()`（放在 `UseCors` 之后、`UseRouting` 之后），
  控制器上用 `[EnableRateLimiting(RateLimitPolicies.Auth)]` 为认证类接口开启配额。
- 配置：`RateLimits:Auth:PermitLimit`（默认 10）、`RateLimits:Auth:WindowSeconds`（默认 60）、
  `RateLimits:Auth:QueueLimit`（默认 0，即立即拒绝）。
- 被限流时返回**框架统一响应体**（`status_code = 4290`）并带 `Retry-After` 头，而不是裸 429。
- **部署前提**：服务在反向代理 / 网关之后时，必须先启用 ForwardedHeaders，
  否则所有请求的 `RemoteIpAddress` 都是代理地址，会让所有人共用一个配额。
- **用户名维度**（已实现）：`ILoginAttemptGuard` 在登录流程内做失败计数与临时锁定 ——
  默认连续 5 次失败锁定 60 秒，锁定期内登录返回 429（业务码 4291）。
  中间件阶段用户名还在请求体里，强行读取请求体既昂贵又不可靠；
  而且把 IP 与用户名拼成一个分区键反而更弱 —— 换个用户名就是新配额。
- 登录保护配置（配置节 `LoginProtection`）：`MaxFailures`（默认 5）、`FailureWindowSeconds`（默认 300）、`LockoutSeconds`（默认 60）。
- **默认实现是进程内的**：多实例部署时每个实例各算一套计数，实际允许次数会成倍放大，
  此时应替换为分布式实现（如 Redis），接口不变。

## 异常与状态码映射

| 异常类型 | HTTP | 业务码 |
|---|---|---|
| `BusinessException` | 400 | 构造时指定 |
| `UnauthorizedBusinessException` | 401 | 构造时指定 |
| `ForbiddenBusinessException` | 403 | 默认 4030 |
| `NotFoundException` | 404 | 默认 4040 |
| `BusinessConflictException` / `ConcurrencyConflictException` | 409 | 4090 |
| `TooManyRequestsException` | 429 | 默认 4290；账号锁定用 4291 |
| `DownstreamServiceException` | 502 | 构造时指定 |
| `DownstreamTimeoutException` | 504 | 构造时指定 |
| **其它一切异常**（含 BCL 的 `ArgumentException`、`InvalidOperationException`、`KeyNotFoundException`） | 500 | 5000 |

- 只有框架定义的业务异常会把异常消息透传给调用方；其余异常统一返回「服务器内部错误」并附带 `error_id`（便于对照日志排查）。
- 日志级别由映射出的状态码决定：5xx 记 Error（含堆栈），4xx 记 Warning —— 规则只有一处，不会随时间跑偏。
- 开发环境下 5xx 响应会附带 `error_detail`（堆栈）；生产环境不返回。请求体与 Authorization 头在写日志前一律脱敏。
