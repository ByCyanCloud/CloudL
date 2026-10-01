## [未发布]

### 变更（SqlSugar 侧，真达梦实例实测得出）

- **达梦字符串列改用 `NVARCHAR2(256)`**（原 `VARCHAR(256)`）：达梦的 `VARCHAR(n)` 按**字节**计，256 字节只装得下 85 个汉字；`NVARCHAR2(n)` 按**字符**计，与 EF 侧 `IsUnicode(true)` 意图一致。⚠️ **已有达梦库的字符串列类型会因此变化**，请核对/迁移。
- **Guid 主键列类型按 provider 对齐 EF 建出的库**：达梦 `CHAR(36)`、PostgreSQL `uuid`、SqlServer `uniqueidentifier`、MySql `char(36)`（Oracle 保持 SqlSugar 默认，未验证）。此前 SqlSugar 默认 `varchar(36)` 与 EF 各 provider 都不一致，会让 `InitTables` 反复 ALTER。
- **新增 `SqlSugarOptions.IsAutoToUpper`**（默认 `true`，即 SqlSugar 原默认）：达梦实例 `CASE_SENSITIVE=1` 且库表为小写时**必须设为 `false`** —— 否则 ORM 去找 `"ORGANIZATIONS"` 而库里是 `"organizations"`，报「无效的表或视图名」；而原生 SQL 用引号小写却正常，极易误判为数据没同步。
- 修复**类型串被重复拼长度**：SqlSugar 会把 `column.Length` 再拼一次 → `NVARCHAR2(256)(200)`（达梦语法错），连迁移执行器自己的历史表都建不出来；框架自己写完整类型串时把 `Length` 归零。
- 主键列强制 `NOT NULL`（模型与数据字典一致），否则**第二次** `InitTables` 会生成改可空的 ALTER 并在达梦上报错。

# 更新日志

本文件记录框架每个版本的变化，并对**是否需要业务侧动作**给出明确声明。
格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。
契约分级与各节的权威描述见 [CONTRACT.md](https://github.com/ByCyanCloud/CloudL/blob/main/CONTRACT.md)。

## [0.7.1] - 2026-09-30

### 修复

- **SqlSugar 列约定此前完全不生效**：SqlSugar 会把 `DataType` 预填成自己的默认值（`DateTime` → `TIMESTAMP`），
  因此"未配置才套默认值"的判断永不命中 —— **时间列不带时区**与**字符串默认长度**两条约定一条都没生效。
  现改为按 `[SugarColumn(ColumnDataType=…)]` 判断是否显式配置。
- **显式设置 `DataType` 会丢掉可空性**，把可空列建成 `NOT NULL`（已按 CLR 类型补回）。
- **墙上钟守卫补齐到 SqlSugar**：此前只有 EF 侧有，SqlSugar 写 `Kind=Utc` 到 PostgreSQL 会抛异常。
- **审计人字段补齐到 SqlSugar**：`created_by`/`updated_by` 此前恒为 NULL 且无任何报错。
- **`FindSingleAsync` 统一 `Single` 语义**（此前用 `FirstAsync`，多条匹配时静默取首条，与 EF 不一致）。
- **EF 侧 `RowVersion` 推进范围**：此前只对 `IAuditable` 推进，普通实体的并发令牌永不前进。
- **`EnableInitTables` 从死选项变为真正接线**（需配合 `InitTablesEntityTypes`；留空则启动即失败）。
- SqlSugar 事务深度只在 `finally` 递减一次（此前提交抛异常会被双重递减，导致写操作**静默脱离事务**）。
- `EnableSqlLog` 改走 `ILogger`（不再用同步 `Console.WriteLine` 绕过 Serilog）。

### 新增

- `ISqlSugarRepository<,>`：把**带期望令牌的乐观锁更新**暴露到接口上（此前只在具体类上，注入 `IRepository` 的调用方调不到）。
- 达梦真库测试（列约定/CRUD/事务回滚）：设 `CLOUDL_DM_CONNECTION` 后运行，未配置时**显式 Skipped**。

### 迁移影响

- **需要业务侧新增 EF 迁移：否。破坏性变更：否。**
- ⚠️ **SqlSugar 用户请核对表结构**：0.7.0 的列约定实际未生效 —— 若你已用 `InitTables` 或迁移建过表，
  时间列类型可能与预期不同（例如字符串不是 256 长度、时间列类型不符）。建议按新版本**重新生成或核对** schema。

## [0.7.0] - 2026-09-29

### 新增

- **`Sha256SaltedHash`（`CloudL.Core/Domain.Shared/Security`）**：把"单次 SHA-256 + 随机盐"这一算法纳入框架
  （格式 `sha256$<Base64盐>$<Base64哈希>`，**盐在前**）。供存量哈希兼容与机构密钥等场景使用；
  用户口令仍应使用框架默认的 **PBKDF2**，并在校验通过后按 `NeedsRehash` 升级。
- **SqlSugar 领域事件**：写操作成功后由仓储喂入工作单元；事务内**延迟**、**提交后分发**、**回滚丢弃**
  （不会出现"通知发出去了、数据却没落库"的幽灵事件）；实体上的事件立即清空，避免重复分发。
- **SqlSugar 乐观锁**：`UpdateAsync(entity, expectedRowVersion)` 按期望令牌做条件更新，影响 0 行即抛
  `ConcurrencyConflictException` 并**不分发领域事件**（与 EF 的 `DbUpdateConcurrencyException` 语义对应）。

### 修复

- SqlSugar 侧**声明主键**：`EntityService` 现在把 `Entity<TKey>.Id` 标为 `IsPrimarykey` —— 此前 EF 的
  `BaseEntityConfiguration` 声明对 SqlSugar 不可见，`Updateable(entity)` 会抛
  "You cannot have no primary key and no conditions"。
- **DI 注册**：`SqlSugarUnitOfWork` 同时以具体类型与 `IUnitOfWork` 注册（否则仓储拿不到它，
  **领域事件永远不会被收集**，且完全静默）。

### 迁移影响

- **需要业务侧新增 EF 迁移：否。破坏性变更：否**（均为新增）。
- SqlSugar 的乐观锁**必须显式传期望令牌**（无变更跟踪，拿不到原值）。

## [0.6.1] - 2026-09-29

### 修复

- **0.6.0 静默漏发了两个新包**：`build/pack.ps1` 用**显式项目数组**决定打包范围，
  而 `CloudL.SqlSugar` 与 `CloudL.EntityFrameworkCore.Dm` 没有被列进去 —— 它们从未进入 `local-feed`，
  工作流的 `local-feed/*.nupkg` 通配推送自然无从推它们；而发布后校验只轮询 `cloudl.core` 与 `cloudl.templates`，
  **于是 run 是绿的、两个包却没发** ✗。
- 已把两个项目补进 `pack.ps1`；本版本重新发布，使这两个包**首次上架**为 0.6.1（其余 6 个包由 `--skip-duplicate` 跳过）。
- **`FrameworkDbContext.ConfigureConventions` 的达梦分支嵌在 Npgsql 判断内部 —— 静默失效** ✗：
  "时间列用 `TIMESTAMP`"的达梦约定被写在了 `ProviderName.Contains("Npgsql")` 的 `if` **内部**，
  而达梦的 provider 名是 `DM.Microsoft.EntityFrameworkCore`（不含 `Npgsql`）——
  外层条件恒为 false，这段分支**一次也不会执行**，并且**不报错、不告警**：
  时间列类型悄悄退化成 provider 默认值，只有**非 Npgsql 的提供程序（达梦等）**才会暴露；
  Npgsql 侧一直正常，所以这个 bug 能潜伏很久。已改为两个**并列**分支（`if` / `else if`），
  **Npgsql 与达梦两者都保留**，并在注释里写明原委；同时补了回归测试
  `ProviderTimeColumnConventionTests`（达梦侧断言 `TIMESTAMP`、Npgsql 侧断言 `timestamp without time zone`，
  两条缺一不可）钉住它。达梦 provider 的默认类型本来就是 `TIMESTAMP`，故**既有库与既有迁移无需改动**
  （这也正是它此前没有暴露的原因）。

### 迁移影响

- **需要业务侧新增 EF 迁移：否。破坏性变更：否。**
- 框架维护者：`CloudL.SqlSugar` 与 `CloudL.EntityFrameworkCore.Dm` 的包校验基线应在本次上架后改为 `0.6.1`。

## [0.6.0] - 2026-09-27

### 新增

- **`CloudL.SqlSugar` 包**：SqlSugar 持久化实现（适用于**达梦等国产数据库**），与 `CloudL.EntityFrameworkCore` **并列二选一**；
  提供 `AddCloudLSqlSugar(...)`、`SqlSugarRepository<,>`（与 `IRepository<,>` 同一份契约）、`SqlSugarUnitOfWork`。
- **中立持久化标记** `PersistenceProvider`：同一应用同时注册 EF 与 SqlSugar 时**启动即失败**，避免两套审计/事务/迁移语义造成数据不一致。
- **中立忽略声明** `NotPersistedAttribute`：忽略规则**只写一份**（已用于 `BaseEntity.DomainEvents` —— 此前 SqlSugar 会把它当列写并抛异常）。
- **`Entity<TKey>.AssignId`**：主键补齐入口（**仅供持久化实现/测试**，业务不要调用）。
- **`CloudL.EntityFrameworkCore.Dm` 包**：达梦（DM）EF Core 提供程序（`UseCloudLDm`）。⚠️ 依赖达梦官方 **EF Core 9** 版提供程序而框架用 EF Core 10，属**未经真实达梦实例验证**的组合；官方发布 EF10 版后应立即升级。`DM.DmProvider` 已直接引用并钉在 `8.3.1.36935`（官方声明的 `8.3.1.33719` 不在 nuget.org）。

### 变更

- SqlSugar 侧时间列按 `DbType` 映射为**不带时区**类型（达梦 `TIMESTAMP`），与 EF 侧一致。
- SqlSugar 侧字符串默认长度引用**同一个常量** `AppConstants.DefaultStringMaxLength`。
- SqlSugar 的 `UpdateAsync` 通过 `IAuditable` 刷新 `UpdatedAt`。

### 迁移影响

- **需要业务侧新增 EF 迁移：否。破坏性变更：否**（均为新增）。
- SqlSugar 与 EF 有**三个固有差异**（无变更跟踪、主键不自动生成、回滚只能靠事务），详见 CONTRACT.md。
- 框架维护者注意：`CloudL.SqlSugar` 首次发布前关闭了包校验基线，**0.6.0 发布后应改为 `PackageValidationBaselineVersion=0.6.0`**。

## [0.5.0] - 2026-09-27

### 变更（破坏性：仓储契约分层）

- 把 **EF Core 专属**的两个成员从通用契约 `IRepository<TEntity, TKey>` **下移**到新增的
  `IEfCoreRepository<TEntity, TKey>`（位于 `CloudL.EntityFrameworkCore`）：
  `GetQueryable()` 与 `ToListAsync<TResult>(IQueryable<TResult>)`。
- `IEfCoreRepository<,>` **继承** `IRepository<,>` —— 继承它的业务接口**能力只增不减**。
- 分层原因：这两个成员依赖 **LINQ 提供程序**，除 EF Core 外的实现（如 SqlSugar 的 `ISugarQueryable`）
  **无法实现 `IQueryable<T>`**，放在通用契约里会让"通用仓储"绑死 EF。
- `AddCloudLEntityFrameworkCore` 现在**同时注册**两个契约（指向同一个 `EfCoreRepository<,>`）。

### 迁移影响

- **需要业务侧新增 EF 迁移：否。**
- ⚠️ **破坏性变更：是**（`IRepository` 少了两个成员）：
  - 若你的仓储接口继承了 `IRepository` **且应用层调用过** `GetQueryable()` / `ToListAsync(IQueryable<>)`
    → 升级后**编译报错**（不会静默失效）→ 把业务接口的基接口改为 `IEfCoreRepository<...>` 即可；
    **应用层与仓储实现类都无需改动**（基类已实现新契约）。
  - 若未用过这两个方法 → **零改动**。
- 本次中断已登记在 `src/CloudL.Core/CompatibilitySuppressions.xml`（有意为之的 API 中断）。
## [0.4.1] - 2026-09-27

### 新增

- **启动时输出运行环境与监听地址**：由 `AddCloudLAspNetCore` 自动注册（**零配置**，业务项目无需改
  `Program.cs`），在 `ApplicationStarted`（服务器**已开始监听之后**）输出一行，例如：
  `服务已启动 —— 应用=MyApp，运行环境=Production（生产环境），监听地址：http://0.0.0.0:10003`。

### 修复

- **日志排版**：块状日志的换行从"消息开头"改为由**输出模板最前面的 `{NewLine}`** 产生 ——
  原先那个换行会落在 Serilog 的 `[级别]` 前缀之后，导致**前缀独占一行、标题与正文分离**。
  模板项目已默认改好；**项目自带的 `appsettings.json` 需要自行调整**（见迁移影响）。

### 其它

- 冒烟测试改用**独立 NuGet 包目录**，避免"框架重新打包但版本号未变"时链接到全局缓存里的旧副本
  ——那会造成"冒烟通过、实际测的是旧二进制"的假绿。
- 新增集成测试：用真实宿主 + 捕获 logger 断言启动信息确实被输出（可长期回归）。

### 迁移影响

- **需要业务侧新增 EF 迁移：否。破坏性变更：否。**
- ⚠️ 若要日志"每条之前一个空行"，请在自己的 `appsettings.json` 里把 Serilog 的 `outputTemplate`
  改成以 `{NewLine}` 开头（模板项目已默认改好）。
## [0.4.0] - 2026-09-27

### 变更（破坏性：依赖大版本升级）

- **Swashbuckle.AspNetCore 6.9 → 10.2**（连同 `Microsoft.OpenApi` 1.6 → 3.x）：
  `Microsoft.OpenApi.Models` 并入根命名空间；`OpenApiSecurityScheme.Reference` / `OpenApiReference` 被移除
  （改用 `OpenApiSecuritySchemeReference`）；`OpenApiSecurityRequirement` 的 scopes 由 `string[]` 改为 `List<string>`；
  `AddSecurityRequirement` 改收 `Func<OpenApiDocument, …>`。框架内部已全部适配；
  **业务项目若自己用过这些类型需要同步改**。
- **Serilog.AspNetCore 8.0 → 10.0**（模板侧 Sinks 同步升到 Console 6.1.1 / File 7.0.0，否则生成的项目会 NU1605）。
- **Mapster 7.4 → 10.0**、**Microsoft.NET.Test.Sdk 17.12 → 18.10**、**xunit.runner.visualstudio 2.8 → 4.0**、**coverlet 6.0 → 10.0**。
- 补丁级同步升级：EF Core 与 Microsoft.Extensions 10.0.9 → 10.0.12、JwtBearer/TestHost 10.0.0 → 10.0.12、
  Npgsql 10.0.2 → 10.0.3、xunit 2.9.2 → 2.9.3。
- **日志排版修正**：块状日志的换行从"消息开头"改为由**输出模板最前面的 `{NewLine}`** 产生 —— 原先换行落在
  `[级别]` 前缀之后，导致前缀独占一行（日志看起来"标题与正文分离"）。

### 迁移影响

- **需要业务侧新增 EF 迁移：否。**
- ⚠️ **破坏性变更：是**（依赖大版本）。业务项目升级后如出现 NU1605 包降级，说明项目侧钉了更低的版本
  —— 请**不要钉框架已传递的包**（EF Core / Serilog / Swashbuckle 等）。
- ⚠️ 日志模板：若项目自带 `appsettings.json` 的 `outputTemplate`，请改成以 `{NewLine}` 开头，才能得到
  "每条日志前一个空行"的效果（模板项目已默认改好）。
- 未升级项：`SQLitePCLRaw.lib.e_sqlite3` 固定 2.1.13（3.x 与 EF 带来的 `bundle_e_sqlite3 2.1.11` 不兼容）。
## [0.3.2] - 2026-09-25

### 新增

- **时间墙上钟保证**：新增 `TimeOptions.ToWallClock(DateTime)` / `(DateTime?)`（实例与静态 `CloudLTime` 各一份）。
  `Unspecified` 原样返回（它本身就是墙上钟），只有 `Utc`/`Local` 才换算 —— 顺序不能反：
  `new DateTimeOffset(Unspecified)` 会把它当作服务器本地时间，导致墙上钟再被套一层偏移。
- **写入守卫**：`SaveChanges` 前扫描 `Added`/`Modified` 实体的 `DateTime`/`DateTime?` 属性，
  `Kind != Unspecified` 一律换算成本口径的墙上钟。此前 `Kind=Utc` 在 PostgreSQL 上会直接抛异常
  （表现为 500），在 SqlServer/SQLite 上却会静默存进去 —— 现在三种库行为一致。

### 迁移影响

- **需要业务侧新增 EF 迁移：否。**
- **破坏性变更：否**（均为新增；`extraClaims` 是可选参数，现有调用不受影响）。
- 行为变化：写入的 `DateTime` 会被归一为墙上钟（原先在 PostgreSQL 上写 `Kind=Utc` 会 500、
  在 SqlServer 上会带着 Utc Kind 落库，属缺陷修正）。
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
