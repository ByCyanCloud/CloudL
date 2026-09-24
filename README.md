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
