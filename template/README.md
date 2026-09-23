# TemplateProject

基于 **Livia 框架**（NuGet 包）的 DDD 九层 Web API 业务骨架，目标框架 .NET 10。

> 本 README 由模板生成，请按项目实际情况修改。

## 快速开始

```powershell
dotnet restore
dotnet build

# 生成初始迁移（模板不预置迁移，迁移属于各业务项目）
dotnet ef migrations add InitialCreate --project src/TemplateProject.EntityFrameworkCore

# 应用迁移并写入角色种子数据
dotnet run --project src/TemplateProject.DbMigrator

# 启动 API（开发环境含 Swagger）
dotnet run --project src/TemplateProject.Web
```

Swagger：`http://localhost:10003/swagger`　健康检查：`http://localhost:10003/health`

## 九层结构与职责

| 层 | 职责 | 框架能力来自 |
|---|---|---|
| `Domain.Shared` | 业务枚举、常量（如角色码） | `Livia.Core`（`AppConstants`/`ErrorCodes`/业务异常基类） |
| `Domain` | 实体、值对象、仓储接口、领域事件 | `Livia.Core`（`AuditableEntity`/`ValueObject`/`IRepository`/`IDomainEvent`） |
| `Application.Contracts` | DTO、服务接口 | `Livia.Core`（`PagedRequestDto`/`IPasswordHasher`/`ICurrentUser` 等契约） |
| `Application` | 用例编排、DTO 映射、领域事件处理器 | `Livia.Core`（领域事件分发、Mapster 全局约定） |
| `Infrastructure` | 业务侧基础设施（邮件、短信、第三方适配） | `Livia.AspNetCore`（`IHttpClientService`/`IPasswordHasher` 实现） |
| `EntityFrameworkCore` | `AppDbContext`、实体配置、**迁移** | `Livia.EntityFrameworkCore`（`FrameworkDbContext`/仓储/配置基类） |
| `HttpApi` | 控制器 | `Livia.AspNetCore`（`BaseApiController`/统一响应/异常与验证） |
| `Web` | 启动与配置 | `Livia.AspNetCore`（`AddLiviaAspNetCore`/JWT/CORS/Swagger） |
| `DbMigrator` | 数据库迁移与种子数据 | `Livia.EntityFrameworkCore` |

### ⚠️ 重要约定：框架代码不在本仓库里

`Entity<TKey>`、`IRepository<,>`、`FrameworkDbContext`、`ExceptionHandlingMiddleware`、`ApiResponse`、
`JwtTokenService`、`Pbkdf2PasswordHasher` 等**全部来自 Livia 的三个 NuGet 包**，本仓库只放业务代码。

- ❌ 不要把框架代码复制进本项目 —— 那会造成分叉，之后无法跟随框架升级。
- ✅ 需要改框架行为时，优先用框架提供的扩展点：Options 配置、覆写 `FrameworkDbContext` 的虚方法、
  替换 DI 注册（如自定义 `IRefreshTokenStore`）。
- ✅ 确实要改框架本身，请到框架仓库提 PR。

各层的「应该放什么 / 不应该放什么」详见 `src/TemplateProject.Infrastructure/README.md`。

## 框架升级

三个包锁步同版本，版本只在 `Directory.Packages.props` 里出现一次：

```powershell
# 升级到可用最新版本
dotnet package update

# 或指定版本
dotnet package update Livia.Core Livia.EntityFrameworkCore Livia.AspNetCore

# 回滚
dotnet package update Livia.Core@0.1.0
```

升级前请先看框架 `CHANGELOG.md` 中的**「迁移影响」**：若声明需要新增 EF 迁移，执行

```powershell
dotnet ef migrations has-pending-model-changes --project src/TemplateProject.EntityFrameworkCore
dotnet ef migrations add <Name> --project src/TemplateProject.EntityFrameworkCore
```

### 生成时指定框架版本

```powershell
dotnet new livia -n MyApp --frameworkVersion 1.2.0
```

模板默认使用浮动版本（`0.1.0-*`，即 0.1 线的最新预览版），便于本地开发始终取到最新构建；
`Directory.Packages.props` 中已开启 `CentralPackageFloatingVersionsEnabled` 以允许此写法。
**正式项目建议把 `LiviaVersion` 固定为具体版本**，让构建可复现。

### 本地联调（框架包尚未发布到 nuget.org 时）

先把框架仓库的本地源注册到本机（只需一次）：

```powershell
dotnet nuget add source <框架仓库路径>/local-feed -n livia-local
```

之后 `dotnet restore` 即可从本地源解析 `Livia.*` 包。

## 必须修改的配置

上线前请务必处理 `src/TemplateProject.Web/appsettings.json`：

- `ConnectionStrings:Default` —— 数据库连接串；
- `Jwt:SecretKey` —— **生产环境必须通过环境变量注入**（至少 32 字符），不要提交到仓库；
- `Cors:AllowedOrigins` —— 前端来源白名单。

配置缺失或非法时应用会在**启动阶段**直接报错（`ValidateOnStart`），不会拖到第一次请求才失败。

## 认证与授权

- 登录：`POST /api/auth/login`；刷新：`POST /api/auth/refresh-token`；登出：`POST /api/auth/logout`；改密：`POST /api/auth/change-password`。
- Refresh Token **一次性使用**（旋转），默认存储为进程内实现；多实例部署请替换 `IRefreshTokenStore`（例如 Redis）。
- 已配置 `FallbackPolicy`：**新增接口默认要求认证**，需要匿名访问时必须显式标注 `[AllowAnonymous]`。
- 角色策略 `AdminOrAbove` / `ManagerOrAbove` 已注册，可直接用于 `[Authorize(Policy = "AdminOrAbove")]`。

## 数据库迁移

```powershell
# 生成迁移（由 EF 项目内置的 DesignTimeDbContextFactory 提供连接串与提供程序）
dotnet ef migrations add <Name> --project src/TemplateProject.EntityFrameworkCore

# 查看待应用的迁移
dotnet ef migrations list --project src/TemplateProject.EntityFrameworkCore

# 应用迁移（或直接运行 DbMigrator）
dotnet run --project src/TemplateProject.DbMigrator
```

`TemplateProject.EntityFrameworkCore` 已引用 `Microsoft.EntityFrameworkCore.Design`（`PrivateAssets=all`），
其内置的 `DesignTimeDbContextFactory` 会从 Web 项目读取 `appsettings.json`，因此**不依赖当前工作目录**。

如需让 `dotnet ef` 通过 Web 主机的依赖注入来构造 `DbContext`，请给 Web 项目也加上
`Microsoft.EntityFrameworkCore.Design`，并追加 `--startup-project src/TemplateProject.Web`。
