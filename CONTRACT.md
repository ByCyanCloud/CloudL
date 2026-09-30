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
| Query 命名 | 只支持 **camelCase**（如 `pageIndex`；MVC 大小写不敏感，PascalCase 亦可）；**Swagger 文档同步显示 camelCase**；请求体/响应体 JSON 仍为 snake_case |
| **时间** | 业务与审计时间**一律不带时区**：库里是墙上钟时间（`timestamp without time zone` / `datetime2`），API 输出**不带 `Z`**；口径由 `Time:Clock` 决定（`Utc` / 固定偏移如 `+08:00` / `Local`），统一走 `CloudLTime` |
| **协议时间** | JWT 的 `exp` / `nbf` 等按 RFC 7519 是 epoch 秒（UTC），**必须用 UTC，不要改成 `CloudLTime`** —— 否则配 `Clock=Local` 后令牌会整体偏移 |
| 统一响应 | `{ success, status_code, message, data \| errors, error_id, error_detail(仅开发环境且 5xx) }` |
| 业务码 | 2000 成功、4000/4001 参数与业务校验、4010/4011 认证、4030 权限、4040 不存在、4090 冲突、4290 限流、4291 账号锁定、5000 服务端、5020/5040 下游 |
| 异常→状态码 | 见 README「异常与状态码映射」；**只有框架定义的业务异常才透传消息**，其它一律 500 + 通用消息 |
| 分页 | 参数 `page_index` / `page_size` / `sort_by` / `sort_direction`；`page_size` 上限 100；排序自动追加主键次级键（保证翻页稳定） |
| 限流 | 429 + `Retry-After`；IP 维度（IPv6 按 `/64` 聚合）+ 账号维度（锁定返回 429/4291） |
| 领域事件 | `SaveChanges` **提交后**分发；事务内则**事务提交后**统一分发，回滚不分发 |
| **日志** | 块状日志的**空行由输出模板的 `{NewLine}` 产生**（放在模板最前），**消息内不夹换行** —— 否则换行会落在 Serilog 的 `[级别]` 前缀之后，前缀会独占一行；请求日志摘要中的敏感 query 参数已脱敏；请求体在 multipart/超大/分块时**不缓冲** |
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

---

## 持久化：一个项目只允许一套 ORM

框架可以**同时提供**两套持久化实现（EF Core 与 SqlSugar）。它们在**框架层面互不干扰**：独立包、独立依赖链、独立 DI 注册，同进程共存没有问题。

但**在同一个项目里并存会出问题**，因此定下三条规矩：

| # | 规矩 | 原因 |
|---|---|---|
| 1 | **一个项目（一个解决方案）只允许一套 ORM 管理同一批表** | 审计字段填充、并发令牌推进、领域事件分发各有一套实现；两条写入路径会让数据"有时对有时不对"，极难排查 |
| 2 | **严禁在同一请求 / 同一事务里混用两套 ORM** | 两者各有独立连接与事务 → 一边提交、一边回滚 → **数据不一致** |
| 3 | **迁移只能由一方管理** | 两边都跑迁移会互相改坏对方的表 |

**ORM 的选择发生在创建项目时**（模板参数 `--orm efcore|sqlsugar`），而不是运行时。

**过渡期若必须分模块/分表并用**，需同时满足：不同表集、不同事务边界、**只有一套负责建表改表**。

> **落地要求（`CloudL.SqlSugar` 包必须满足）**：实现"**启动即失败**"守卫 —— 若同一应用同时注册了 EF 与 SqlSugar 两套持久化，
> 启动直接抛异常并说明"请二选一"。目的是把"数据不一致的隐形 bug"变成"立刻可见的配置错误"。

---

## 仓储契约的分层（IRepository 与 IEfCoreRepository）

| 契约 | 所在项目 | 内容 |
|---|---|---|
| **`IRepository<TEntity, TKey>`** | `CloudL.Core` | **ORM 中立**的通用仓储：`GetByIdAsync` / `FindAsync` / `FindSingleAsync` / `FindSingleForUpdateAsync` / `ExistsAsync` / `AddAsync` / `AddRangeAsync` / `UpdateAsync` / `DeleteAsync` / `DeleteByIdAsync` / `CountAsync` / `GetPagedAsync` |
| **`IEfCoreRepository<TEntity, TKey>`** | `CloudL.EntityFrameworkCore` | 在通用契约之上**额外**提供 EF Core 专属的两个成员：`GetQueryable()`（`IQueryable<TEntity>`，无跟踪）与 `ToListAsync<TResult>(IQueryable<TResult>)` |

**为什么分层**：这两个成员依赖 **LINQ 提供程序**，除 EF Core 之外的实现（如 SqlSugar 的 `ISugarQueryable`）**无法实现 `IQueryable<T>`** —— 放在通用契约里会让"通用仓储"绑死 EF。

**继承关系**：`IEfCoreRepository<,>` **继承** `IRepository<,>`，因此业务接口继承它会**同时**获得两套能力：

```csharp
// 只需要中立能力（推荐：将来换 ORM 无痛）
public interface IUserRepository : IRepository<User, Guid> { }

// 需要 IQueryable 逃生口（EF 项目）
public interface IUserRepository : IEfCoreRepository<User, Guid> { }
```

**升级提示（重要）**：`GetQueryable()` 与 `ToListAsync(IQueryable<>)` 已从 `IRepository` **移到** `IEfCoreRepository`。
若你的仓储接口继承了 `IRepository` **且**应用层调用过这两个方法，升级后会**编译报错**（不会静默失效）——
把业务接口的基接口换成 `IEfCoreRepository` 即可，**应用层代码无需改动**。

---

## SqlSugar 持久化（CloudL.SqlSugar）

```csharp
services.AddCloudLSqlSugar(options =>
{
    options.ConnectionString = configuration.GetRequiredConnectionString();
    options.DbType = "Dm";            // Dm(达梦) / PostgreSQL / SqlServer / MySql / Sqlite / Oracle
    options.EnableInitTables = false; // 仅开发期可开；生产用版本化 SQL 脚本
});
```

注册后可用 `IRepository<,>` / `IUnitOfWork`（与 EF 版**同一份契约**）以及 `ISqlSugarClient`。

### 与 EF 版的固有差异（三个，务必先读）

| # | 差异 | 影响 |
|---|---|---|
| 1 | **没有变更跟踪**：`AddAsync`/`UpdateAsync`/`DeleteAsync` **立即下发 SQL** | 不要依赖「不调用 `SaveChangesAsync` 就不会落库」；`SaveChangesAsync` 固定返回 0 |
| 2 | **主键不会自动生成**（EF 由键生成器填） | 仓储插入前补齐：`Guid` 主键由框架补；其它类型**必须**在建实体时传入，否则抛明确错误 |
| 3 | **回滚只能靠事务** | 需要同生共死的写操作必须放进 `ExecuteInTransactionAsync` |

### 约定与对齐

- 分页与 EF 一致：`pageIndex` **1 基**、总数在排序前统计、**两个方向都追加主键次级排序**（否则并列值翻页会重复或漏行）。
- 列约定与 EF 同源：时间列**不带时区**（按 `DbType` 映射，达梦为 `TIMESTAMP`）；字符串未显式配置时用 `AppConstants.DefaultStringMaxLength`（**同一个常量**）。
- `UpdateAsync` 通过 `IAuditable` 刷新 `UpdatedAt`（覆盖泛型与非泛型两种可审计基类）；`CreatedAt` 由实体基类构造时写入。
- `[NotPersisted]`（`CloudL.Core`）标记的成员**任何 ORM 都不持久化** —— 忽略规则只写一份。
- `Entity<TKey>.AssignId` **仅供持久化实现或测试**在键未生成时补齐，**业务代码不要调用**。
- 一个项目**只允许一套 ORM**：同时注册 EF 与 SqlSugar 会在**启动时失败**。
- ✅ **领域事件已支持**：仓储在写操作成功后把实体事件交给工作单元 → 事务内延迟、**提交后分发**、**回滚丢弃**（不会出现幽灵事件）；实体上的事件立即清空，避免重复分发。
- ⚠️ **乐观锁需显式传期望令牌**：`UpdateAsync(entity, expectedRowVersion)`；因 SqlSugar 无变更跟踪、拿不到原始令牌，无参重载只推进令牌、**不做冲突检测**。
- 📌 **规律**：EF 靠配置声明的元数据（忽略成员如 `DomainEvents`、主键 `Id`），SqlSugar 侧都在 `SqlSugarModule` 的 `EntityService` 里**显式声明过一次** —— 框架基类新增非持久化成员时，**两边都要处理**。
- 迁移方式（方案 C）：开发期可用 `EnableInitTables`，生产用**版本化 SQL 脚本 + 迁移记录表**（执行器为后续版本）。

---

## 达梦（DM）：EF Core 提供程序（CloudL.EntityFrameworkCore.Dm）

```csharp
services.AddCloudLEntityFrameworkCore<AppDbContext>(options =>
    options.UseCloudLDm(configuration.GetRequiredConnectionString()));
```

- 时间列：达梦侧显式使用**不带时区**的 `TIMESTAMP`（与 Npgsql 分支同一意图，见数据库约定）。

### ⚠️ 依赖版本错位（务必先读）

- 达梦官方 EF Core 提供程序最新为 `DM.Microsoft.EntityFrameworkCore 9.0.0.x`（面向 **EF Core 9**），
  而框架本体使用 **EF Core 10.0.12**。该组合**能还原、能编译**（已验证），但 provider 与 EF 强绑定，
  **尚未在真实达梦实例上验证** —— 请先在测试库确认，再上生产。
- 官方发布 EF Core 10 版后，**应立即升级本包依赖**（本包只用 `UseDm`，升级通常只需改版本号）。
- `DM.DmProvider` 被**直接引用**并钉在 `8.3.1.36935`：官方 EF 包声明依赖 `8.3.1.33719`，而该版本不在 nuget.org 上（不直接引用会报 NU1603）。

### 上线前的三步验证

1. `dotnet ef migrations add Init`；
2. 真连达梦执行迁移；
3. 增删改查各一次 + 一次分页。

若出现 `MissingMethodException` 之类的**运行期**错误，说明 EF9↔10 不兼容 → 等官方 EF Core 10 版，
或改用**已经打通并验证**的 `CloudL.SqlSugar`（见上一节）。

---

## 安全：口令与密钥哈希

| 场景 | 用什么 | 说明 |
|---|---|---|
| **用户登录口令** | 框架默认 **PBKDF2**（`IPasswordHasher` 默认实现） | 有工作因子、定长比较、迭代次数上限；**新口令一律用它** |
| **存量哈希兼容** | `CloudL.Domain.Shared.Security.Sha256SaltedHash` | 单次 SHA-256 + 16 字节随机盐，格式 `sha256$<Base64盐>$<Base64哈希>`；**不带工作因子** |
| **机构密钥等** | 同上（可用） | 适用于"库泄露后可接受离线穷举"的场景 |

**存量迁移姿势**：口令校验通过后，用 `IPasswordHasher.NeedsRehash` 判断并**以 PBKDF2 重写** ——
即"存量兼容 + 登录时自动升级"。

⚠️ **拼接顺序与格式不可更改**：`Sha256SaltedHash` 是"**盐在前、明文在后**"（`SHA256(盐 ‖ UTF8(明文))`）。
改顺序或改格式会让**所有存量哈希失效** —— 单元测试里有钉子用例固定这一点（用测试中独立构造的格式串校验）。

