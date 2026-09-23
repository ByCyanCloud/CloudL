# 更新日志

本文件记录框架每个版本的变化，并对**是否需要业务侧动作**给出明确声明。
格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [未发布]

### 新增
- 框架从单体模板改造为 NuGet 包：`Livia.Core`、`Livia.EntityFrameworkCore`、`Livia.AspNetCore`。
- 引入中央包管理（`Directory.Packages.props`）与 MinVer 版本自动化。
- 新增包源映射（`nuget.config`），防御依赖混淆。
- 新增 `FrameworkDbContext`，业务 `AppDbContext` 继承即可获得审计、乐观锁与领域事件分发能力。
- 新增 Options 配置模型（`JwtOptions`、`CorsOptions`、`HttpClients`），并启用启动期校验。
- 新增 PBKDF2 版本化哈希格式与登录透明重哈希。

### 变更
- 命名空间由 `TemplateProject.*` 统一改为 `Livia.*`。
- 全局字符串长度约定（默认 256）改为**仅对未显式配置的属性生效**，显式的 `HasMaxLength` 不再被覆盖。

### 修复
- 修复全局字符串约定覆盖所有 `HasMaxLength` 配置的问题。
- 修复异常中间件与验证过滤器把请求体、Authorization 头原文写入日志的问题。

### 迁移影响
- **需要业务侧新增 EF 迁移**：是。全局字符串约定修复会改变列长度（如 `code` → `string(32)`、`password_hash` → `string(512)`）。
- **破坏性变更**：是。命名空间与配置绑定方式变化，详见升级指南。