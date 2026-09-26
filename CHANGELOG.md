# 更新日志

本文件记录框架每个版本的变化，并对**是否需要业务侧动作**给出明确声明。
格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。
契约分级与各节的权威描述见 [CONTRACT.md](https://github.com/ByCyanCloud/CloudL/blob/main/CONTRACT.md)。

## [0.3.1] - 2026-09-25

### 修复

- **Swagger 文档里的 query 参数名显示为 PascalCase**，与对外约定（camelCase）不一致：0.2.2 停用了旧的
  snake_case 改写过滤器，却没有补上 camelCase，于是文档退回显示 C# 原名。现已新增
  `CamelCaseQueryParameterOperationFilter`，文档显示 `pageIndex`。
  （query 的**实际绑定一直是大小写不敏感的**，所以这不影响能否调用，只影响文档可读性。）

### 变更

- 包校验基线更新为 `0.3.0`，并清理了相对旧基线的抑制条目。

### 迁移影响

- **需要业务侧新增 EF 迁移：否。**
- **破坏性变更：否。** 仅文档显示与构建配置调整。
## [0.3.0] - 2026-09-25

### 变更（破坏性：时间存储与输出格式 + 删除公开类型）

- **时间一律不存储时区**：数据库时间列改为"不带时区"类型（PostgreSQL 由 `timestamptz` 改为
  `timestamp without time zone`；SQL Server 的 `datetime2` 本来就不带）。库里存的就是墙上钟时间。
- **API 输出的时间不再带 `Z`**（此前带 `Z` 表示 UTC）。
- 新增时间口径配置 `Time:Clock`：`Utc`（默认）、固定偏移（如 `+08:00`，与服务器时区无关）、
  `Local`（等价于 `DateTime.Now`，跟随服务器时区）。审计字段按该口径写入。
  ⚠️ 选 `Local` 时必须把运行环境时区钉死（容器 `TZ=Asia/Shanghai`），否则不同环境会写出不同的墙上钟。

### 迁移影响

- ⚠️ **需要重新生成 EF 迁移**：时间列类型已变更。
- **客户端**：不要再假设时间带时区标识，按"服务端约定的本地时间"理解。
- 存量数据：由使用方自行决定（本次未提供数据迁移脚本）。
## [0.2.2] - 2026-09-25

### 修复

- **Swagger 文档与实际接口不一致**（0.2.1 遗留的**功能性**缺陷）：`SnakeCaseQueryParameterOperationFilter` 仍把
  Swagger 里的 query 参数名改写成 snake_case，而 0.2.1 起服务端只接受 camelCase —— 照 Swagger「Try it out」
  发出的请求会导致**分页静默失效**。现已停用该过滤器，Swagger 显示真实可用的参数名。
- 修正随包发布的 **XML 文档注释**：`SnakeCaseQueryValueProvider` 等类型的注释此前仍写着「query 使用 snake_case、
  含大写字母返回 400」，与 0.2.1 的行为相反（IntelliSense 中会误导使用者）。

### 迁移影响

- **需要业务侧新增 EF 迁移：否。** 未改数据库。
- **破坏性变更：否。** 仅修正文档与 Swagger 的一致性。
## [0.2.1] - 2026-09-25

### 修复

- **分页与排序在全平台不可用**（0.2.0 引入的严重缺陷）：snake_case query 键（`page_index`/`page_size`/`sort_by`/
  `sort_direction`）对**复杂对象**（`[FromQuery] PagedRequestDto request`）一个都绑不上 —— 自定义值提供器
  只在简单参数上生效，复杂类型绑定会按其 `BindingSource` 过滤掉它；而 camelCase 又会被命名校验拦成 400。

### 变更（破坏性：query 参数命名约定调整）

- query 参数**只支持 camelCase / PascalCase**（MVC 原生大小写不敏感绑定），**snake_case 支持已移除**：
  停用了 snake_case 值提供器与"含大写字母即 400"的命名校验过滤器。
- 响应 JSON 仍为 snake_case（两者互不影响）。

### 迁移影响

- **需要业务侧新增 EF 迁移：否。**
- ⚠️ **客户端需要改 query 参数名**：`?page_index=2&page_size=20&sort_by=name&sort_direction=asc`
  → `?pageIndex=2&pageSize=20&sortBy=name&sortDirection=asc`。升级前请先改客户端，否则分页会退化为默认值。
- **破坏性变更：是**（对依赖 snake_case query 的调用方），但这是恢复分页/排序可用性的前提。
- 新增受测契约：复杂对象与简单参数的 query 绑定均以 camelCase 验证。
## [0.2.0] - 2026-09-25

> 这一版把框架从"能跑"推到了"可长期依赖"：新增了限流/账号锁定/事务/请求日志，
> 建立了四层自动化防线（API 兼容性校验、138 个测试、包内容安检、冒烟测试），
> 并修掉了一批安全与正确性问题。**破坏性变更较多**，请按下面的「迁移影响」逐条处理。

### 新增

- **请求日志** `UseCloudLRequestLogging()`：每个请求一条结构化摘要（方法、路径、脱敏后的 query、状态码、耗时、TraceId、客户端 IP、用户名）；`/health`、`/swagger`、`/favicon.ico` 降级为 Verbose。
- **IP 维度限流** `AddCloudLRateLimiting()`：认证类接口用 `[EnableRateLimiting(RateLimitPolicies.Auth)]` 开启；被限流返回**框架统一响应体**（`status_code = 4290`）并带 `Retry-After`；IPv6 按 `/64` 前缀聚合（否则换源地址即可绕过）。
- **账号维度锁定** `ILoginAttemptGuard`：连续登录失败达到阈值后临时锁定（默认 5 次 / 锁定 60 秒），返回 429 + 业务码 `4291`；用户名大小写不敏感；带清理节流与跟踪量硬上限，防止被海量用户名撑爆内存。
- **事务助手** `IUnitOfWork.ExecuteInTransactionAsync(...)`：把多次写入包进一个事务，任一步失败整体回滚；支持嵌套（复用最外层事务）；内部走执行策略，兼容 `EnableRetryOnFailure`；**领域事件在事务提交后才分发**，回滚不留事件。
- **query 参数命名校验**：只接受 snake_case（含大写字母直接返回 400），Swagger 文档同步显示 snake_case；同时新增 snake_case 绑定支持。
- **数据库提供程序拆包**：`CloudL.EntityFrameworkCore` 变为**与数据库无关**，提供程序独立为 `CloudL.EntityFrameworkCore.PostgreSql` 与 `CloudL.EntityFrameworkCore.SqlServer`（消费方不再被拖入用不到的提供程序依赖，实测减少 12 个 DLL / 4 MB）。
- **公开 API 兼容性守卫**：打包时与上一个已发布版本比对公开 API 面（底层 ApiCompat），破坏性变更即失败，除非登记到 `CompatibilitySuppressions.xml`（即契约变更台账）。
- **集成测试基座**：TestServer（真实 MVC 管道）+ SQLite 内存库；测试总数 20 → **138**。
- **包内容安检** `build/check-packages.ps1`：禁止配置/证书/私钥混入包，并有规则自测（防止安检静默失效）。
- **模板比对工具** `build/compare-template.ps1`：用当前模板重新生成同名项目，列出"框架装配"文件的差异（模板是复制而非依赖，升级包不会更新它们）。
- **`.editorconfig`**（框架仓库 + 模板）：让代码风格由仓库而非各人 IDE 设置决定。
- **`CONTRACT.md` 对外契约**：契约分级、13 个接口的替换语义、配置键清单、行为约定、数据库约定、升级步骤。

### 变更

- 统一响应中的**字典键不再被改写**：以前会把响应里所有字典的键转成 snake_case（业务字典 `{"USD":"美元"}` 会被改成 `usd`，属语义损坏）。
- **异常映射收紧**：只有框架定义的业务异常才把消息透传给调用方；其它（含 BCL 的 `ArgumentException`、`InvalidOperationException`、`KeyNotFoundException`）一律 500 + 通用消息。
- **新增异常类型** `NotFoundException`（404）与 `ForbiddenBusinessException`（403），`TooManyRequestsException`（429）；异常→状态码映射表见 README。
- 日志级别改为**由映射出的状态码推导**（5xx→Error，4xx→Warning），不再维护第二份"预期异常清单"。
- 健康检查 `/health` 改为**匿名可访问**（此前 FallbackPolicy 会让探针拿到 401）。
- 请求体缓冲收窄：只在"非 multipart、非分块、且体积可控"时启用，避免为上传额外落一份临时文件。
- `SaveChanges` 只遍历一次变更跟踪器（顺带修掉"被删除实体的事件残留"）。
- 框架自身启用**零警告策略**（`TreatWarningsAsErrors`，NuGet 审计类警告除外）。
- 发布工作流：手动触发必须显式指定一个已存在的 `v*` 标签（此前手动触发会算出预览版本并真的推送到 nuget.org）。
- 框架包新增依赖 `Serilog.AspNetCore 8.0.3`（请求日志用；与模板保持一致）。

### 修复

- **分页排序缺少次级排序键**：并列 `created_at` 时翻页会重复或漏行 → 自动追加主键。
- **日志脱敏存在覆盖漏洞**：键名比较不忽略 `_`/`-`，导致 `access_token`、`client_secret`、`api-key` 等未被遮蔽 → 现在忽略分隔符，且新增"按值形态识别"（JWT、≥40 位不透明令牌）；query 串同样脱敏并截断。
- 脱敏后的日志会把中文转义成 `\uXXXX` → 已改用不转义编码器。
- 事务助手在**延迟范围内**分发事件，导致处理器内部触发的事件被静默丢弃 → 改为先结束延迟范围再分发。
- 登录失败计数的清理：高基数时每次失败都全表扫描（CPU 放大）、且活跃攻击下内存无上限 → 加时间节流（默认 30 秒）与跟踪量硬上限（默认 20 万，超出按最久未活动淘汰）。
- 分块传输（`ContentLength` 为 null）的请求仍被缓冲，使"收窄"失效 → 已排除。
- README 包一览只列 3 个包、且写着不存在的 API（`AddFrameworkDbContext`、`AddFramework`）→ 已更正为 6 个包与实际 API 名。

### 迁移影响

**需要业务侧新增 EF 迁移：否**（本版没有改动列名、默认长度等数据库约定）。

**破坏性变更：是。** 逐条如下（⭐ 表示必须处理）：

| # | 变更 | 业务侧要做的事 |
|---|---|---|
| ⭐ 1 | 数据库提供程序拆包，`UseCloudLDatabaseProvider` 与提供程序名常量**已删除** | EF 项目加 `CloudL.EntityFrameworkCore.PostgreSql`（或 `.SqlServer`）包；装配处改为 `services.AddCloudLEntityFrameworkCore<AppDbContext>(options => options.UseCloudLPostgreSql(connectionString))`；`DesignTimeDbContextFactory` 同步；`Database:Provider` 配置键不再使用 |
| ⭐ 2 | `AddCloudLEntityFrameworkCore(IConfiguration)` 与 `(string, string)` 重载**已删除** | 改用接收 `Action<DbContextOptionsBuilder>` 的重载（见上一条） |
| ⭐ 3 | `IRepository<,>.GetAllAsync` 与 `AppConstants.MaxGetAllCount`**已删除** | 改用 `GetPagedAsync`（旧接口会静默截断到 1000 行，属陷阱 API） |
| ⭐ 4 | query 参数**含大写字母直接返回 400** | 客户端改用 snake_case（如 `?pageIndex=2` → `?page_index=2`） |
| ⭐ 5 | 响应中的**字典键不再被改写** | 若客户端依赖旧的"字典键被转成 snake_case"行为，需要适配 |
| 6 | `IUnitOfWork` 新增两个成员 | 仅影响**自行实现**该接口的项目（只使用它则不受影响） |
| 7 | 异常映射收紧：`ArgumentException` / `InvalidOperationException` / `KeyNotFoundException` 不再映射为 400/404 并透传消息 | 若业务代码靠抛这些异常表达"参数错误/资源不存在"，改用 `BusinessException` / `NotFoundException` / `ForbiddenBusinessException` |
| 8 | 模板改用 `NotFoundException` / `ForbiddenBusinessException` | 若你拷贝过模板的 `UserService`，把 `KeyNotFoundException` 与 `UnauthorizedBusinessException(ErrorCodes.Forbidden, …)` 换成新类型 |
| 9 | `/health` 改为匿名 | 若你**有意**让它需要认证，需自行加回授权要求 |
| 10 | 框架包新增 `Serilog.AspNetCore` 依赖 | 若你的项目用其它日志库，会多带一个 Serilog 依赖（可接受） |

**升级步骤**见 `CONTRACT.md` 第 7 节；其中第 4 步（同步模板装配文件）可用：

```powershell
pwsh ./build/compare-template.ps1 -ProjectPath <你的项目根目录>
```

## [0.1.0] - 2026-09-24

### 新增
- 框架从单体模板改造为 NuGet 包：`CloudL.Core`、`CloudL.EntityFrameworkCore`、`CloudL.AspNetCore`。
- 引入中央包管理（`Directory.Packages.props`）与 MinVer 版本自动化。
- 新增包源映射（`nuget.config`），防御依赖混淆。
- 新增 `FrameworkDbContext`，业务 `AppDbContext` 继承即可获得审计、乐观锁与领域事件分发能力。
- 新增 Options 配置模型（`JwtOptions`、`CorsOptions`、`HttpClients`），并启用启动期校验。
- 新增 PBKDF2 版本化哈希格式与登录透明重哈希。

### 变更
- 命名空间由 `TemplateProject.*` 统一改为 `CloudL.*`。
- 全局字符串长度约定（默认 256）改为**仅对未显式配置的属性生效**，显式的 `HasMaxLength` 不再被覆盖。

### 修复
- 修复全局字符串约定覆盖所有 `HasMaxLength` 配置的问题。
- 修复异常中间件与验证过滤器把请求体、Authorization 头原文写入日志的问题。

### 迁移影响
- **需要业务侧新增 EF 迁移**：是。全局字符串约定修复会改变列长度（如 `code` → `string(32)`、`password_hash` → `string(512)`）。
- **破坏性变更**：是。命名空间与配置绑定方式变化。
