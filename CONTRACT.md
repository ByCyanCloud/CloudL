# CloudL 对外契约

这份文件回答一个问题：**框架的哪些部分是"承诺"，哪些只是"实现"？**
用途有两个：业务项目判断"我能不能依赖这个"；框架维护者判断"改这里会不会伤到存量项目"。

> 约定：本文件是**行为与约定的权威来源**。`CHANGELOG.md` 的「迁移影响」直接引用本文件的分节名。

---

## 1. 契约分级

| 级别 | 含义 | 典型内容 |
|---|---|---|
| 🔒 **冻结** | 改动会影响**每一个**业务项目或客户端，只在 **major** 版本动，且必须给迁移说明 | 数据库列名与默认长度、配置键名、统一响应结构、业务码、异常→状态码映射、JSON/query 命名约定 |
| ⛔ **可加不可改** | 可以新增，但**不能改也不能删**已有的 | 公开类型、公开成员签名、接口成员（新增接口成员对"自行实现该接口"的项目同样是破坏性的） |
| 🔧 **实现细节** | 可以自由改（包括删除），不要依赖它 | `internal` 成员、日志文本、临时目录、内部缓存策略、`InMemory*` 实现的内部结构 |

**执行方式**：打包时由包校验（ApiCompat）自动比对上一个已发布版本 —— 破坏性变更会让打包失败，除非在对应的
`CompatibilitySuppressions.xml` 里显式登记（那份文件就是契约变更台账，条目数随版本增长）。

---

## 2. 给业务项目用的 vs 不要直接用的

**业务代码应当只依赖这些**：

| 用途 | 类型 |
|---|---|
| 领域建模 | `Entity<TKey>` / `AuditableEntity<TKey>` / `ValueObject` / `BaseEntity` / `IAuditable` / `IAggregateRoot` / `IDomainEvent` |
| 持久化 | `IRepository<TEntity, TKey>` / `IUnitOfWork` / `PagedResult<T>` / 4 个实体配置基类 / `FrameworkDbContext`（仅继承） |
| 应用层 | `CloudL.Application.Contracts.*`（分页 DTO、服务接口）/ Mapster 全局约定 |
| Web 层 | `BaseApiController` / `ApiResponse` / `ApiResponse<T>` / `ValidationFilter`（在 `Program.cs` 注册） |
| 配置 | `JwtOptions` / `CorsOptions` / `PasswordHasherOptions` / `RateLimitOptions` / `LoginProtectionOptions` / `HttpClients` |
| 装配入口 | `AddCloudLAspNetCore()` / `AddCloudLEntityFrameworkCore<TContext>()` / `UseCloudL*()` 系列 / `UseCloudLPostgreSql()` 或 `UseCloudLSqlServer()` |

**以下属于实现，请通过上表的接口替换，而不是直接依赖**：
`EfCoreRepository<,>`、`EfCoreUnitOfWork`、`Pbkdf2PasswordHasher`、`JwtTokenService`、`InMemoryRefreshTokenStore`、
`InMemoryLoginAttemptGuard`、`HttpClientService`、`CurrentUser`、`SensitiveDataRedactor`、`DomainEventDispatcher`。

---

## 3. 13 个接口的替换语义

| 接口 | 默认实现 | 替换方式 | 替换时的坑 |
|---|---|---|---|
| `IRepository<TEntity,TKey>` | `EfCoreRepository<,>` | 在 `AddCloudLEntityFrameworkCore` **之后**再注册一次（后注册者生效） | 需要自己实现全部成员；注意审计/并发令牌由 `FrameworkDbContext` 负责，不要重复实现 |
| `IUnitOfWork` | `EfCoreUnitOfWork` | 同上 | 必须保持 `ExecuteInTransactionAsync` 的语义：提交后才分发领域事件 |
| `IPasswordHasher` | `Pbkdf2PasswordHasher` | 注册自定义实现 | **换算法必须有迁移路径**：旧哈希要能验证（参考 `NeedsRehash` 的透明升级机制），否则老用户无法登录 |
| `IRefreshTokenStore` | `InMemoryRefreshTokenStore` | 注册分布式实现（如 Redis） | ⚠️ 默认实现是**单进程**的：多实例部署会让刷新令牌校验随机失败 |
| `ILoginAttemptGuard` | `InMemoryLoginAttemptGuard` | 注册分布式实现 | ⚠️ 同上：多实例会成倍放大允许的失败次数 |
| `ICurrentUser` | `CurrentUser`（读 JWT Claims） | 一般不需要替换 | 字段名（`UserId`/`UserCode`/`OrganizationCode`）由 JWT 声明约定决定 |
| `IJwtTokenService` | `JwtTokenService` | 一般不需要替换 | 目前只支持**对称密钥**（HS256），无 `kid`/轮换 |
| `IHttpClientService` | `HttpClientService` | 一般不需要替换 | 具名客户端定义在配置段 `HttpClients` 下 |
| `IDomainEventDispatcher` | `DomainEventDispatcher` | 注册自定义分发器（如接入 Outbox/消息总线） | 分发时机由 `FrameworkDbContext` 决定（`SaveChanges` 提交后；事务内则事务提交后） |
| `IDomainEventHandler<TEvent>` | 你的处理器 | `services.AddDomainEventHandler<TEvent, THandler>()` | 处理器抛异常会导致调用方收到失败响应；事务内事件在提交后才分发 |
| `IDomainEvent` | 你的事件 | 直接实现 | 建议用 `record`；包含 `OccurredAt` |
| `IAuditable` | `AuditableEntity<TKey>` | 自行实现（复合主键场景） | 审计字段由 `FrameworkDbContext` 按 `EntityState` 填充 |
| `IAggregateRoot` | 标记接口 | 直接标记 | 框架不据此做任何行为，仅表达领域意图 |

---

## 4. 🔒 配置键（冻结）

| 配置键 | 必需 | 默认 / 说明 |
|---|---|---|
| `ConnectionStrings:Default` | ✅ | 为空或缺失时**启动即抛异常**（`GetRequiredConnectionString`） |
| `Jwt:SecretKey` | ✅ | 至少 32 字符；缺失/过短启动即失败 |
| `Jwt:Issuer` / `Jwt:Audience` | ✅ | 签发与校验方 |
| `Jwt:ExpirationHours` | | 8 |
| `Jwt:RefreshTokenExpirationDays` | | 7 |
| `Jwt:ClockSkewSeconds` | | 60 |
| `Cors:AllowAnyOrigin` / `AllowCredentials` / `AllowedOrigins` | | 默认不允许任意来源 |
| `PasswordHasher:Iterations` | | 220000（PBKDF2-SHA512）；提升后登录时透明重哈希 |
| `HttpClients:<名称>:{BaseUrl,TimeoutSeconds,ForwardAuthorization}` | | 具名 HTTP 客户端 |
| `RateLimits:Auth:{PermitLimit,WindowSeconds,QueueLimit}` | | 10 / 60 / 0（认证接口按 IP 限流） |
| `LoginProtection:{MaxFailures,FailureWindowSeconds,LockoutSeconds,MaxTrackedAccounts,CleanupIntervalSeconds}` | | 5 / 300 / 60 / 200000 / 30（账号维度锁定） |

**改了会怎样**：`ValidateOnStart` 会在**启动阶段**以明确路径报错 —— 响亮失败，但仍然是破坏性变更，只在 major 做。

---

## 5. 🔒 行为契约（Web 层）

| 契约 | 内容 |
|---|---|
| JSON 命名 | 属性名 `snake_case`；**字典键原样保留**（`{"USD":"美元"}` 不会被改成 `usd`）；忽略 `null`；中文不转义 |
| Query 命名 | 只支持 camelCase / PascalCase（MVC 大小写不敏感绑定）；**snake_case 已移除**；响应 JSON 仍为 snake_case |
| 统一响应 | `{ success, status_code, message, data \| errors, error_id, error_detail(仅开发环境且 5xx) }` |
| 业务码 | 2000 成功、4000/4001 参数与业务校验、4010/4011 认证、4030 权限、4040 不存在、4090 冲突、4290 限流、4291 账号锁定、5000 服务端、5020/5040 下游 |
| 异常→状态码 | 见 README「异常与状态码映射」；**只有框架定义的业务异常才透传消息**，其它一律 500 + 通用消息 |
| 分页 | 参数 `page_index` / `page_size` / `sort_by` / `sort_direction`；`page_size` 上限 100；排序自动追加主键次级键（保证翻页稳定） |
| 限流 | 429 + `Retry-After`；IP 维度（IPv6 按 `/64` 聚合）+ 账号维度（锁定返回 429/4291） |
| 领域事件 | `SaveChanges` **提交后**分发；事务内则**事务提交后**统一分发，回滚不分发 |
| 日志 | 块状日志以空行开头；请求日志摘要中的敏感 query 参数已脱敏；请求体在 multipart/超大/分块时**不缓冲**（日志中为空） |
| 健康检查 | `/health` **匿名可访问**（有自动化守卫） |

---

## 6. 🔒 数据库约定（爆破半径最大）

| 约定 | 值 |
|---|---|
| 列名 | `id` / `created_at` / `row_version` / `updated_at` / `created_by` / `updated_by` |
| 字符串默认长度 | 256（**仅对未显式配置 `MaxLength` 的属性生效**） |
| 并发令牌 | `row_version`（`IsConcurrencyToken`），更新时自动推进 |
| 表名 | 由业务实体配置决定，框架不干预 |

⚠️ **改动这一节的任何一条，都意味着每个业务项目都要重新生成迁移**。
因此：只在 **major** 版本动，并在 CHANGELOG 的「迁移影响」中明确写出需要执行 `dotnet ef migrations add`。

---

## 7. 升级一个业务项目（推荐步骤）

1. 读 `CHANGELOG.md` 对应版本的「**迁移影响**」；
2. 升级框架包：改 `Directory.Packages.props` 里的 `CloudLVersion`（或 `dotnet package update`）；
3. 编译 —— 若有 `[Obsolete]` 警告，按提示改（框架会保留一个 major 的过渡期）；
4. 用框架仓库里的比对脚本同步**装配类文件**（模板生成的 `Program.cs` / `*Module.cs` / 设计时工厂等）：
   ```
   pwsh ./build/compare-template.ps1 -ProjectPath <你的项目根目录>
   ```
5. 若 CHANGELOG 提示需要，重新生成迁移；
6. 跑你自己的测试 + 冒烟（登录、分页、异常响应）。

**注意**：升级 NuGet 包**不会**更新模板生成的那些文件 —— 它们是"复制"而非"依赖"，第 4 步就是为它们准备的。

---

## 8. 框架维护者自查清单（改这些要特别小心）

- 🔒 数据库约定与配置键 → 只在 major 动，且必须写迁移说明；
- ⛔ 公开类型/成员/接口 → 由 ApiCompat 拦，破了就要在抑制文件里登记**理由**；
- 行为契约 → 先改框架测试（121 个）再改实现，让测试替你通知用户；
- 模板生成的文件 → 改动要能被第 4 步的比对脚本发现（保持文件路径稳定，别随意重命名）；
- 框架与模板**共同引用**的第三方包（如 `Serilog.AspNetCore`）→ 两边必须同步升级。
