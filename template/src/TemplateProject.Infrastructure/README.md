# TemplateProject.Infrastructure

本层只放**业务侧**的基础设施实现。

## 这里应该放什么

- 邮件 / 短信发送实现
- 文件存储（本地、OSS、S3）
- 第三方 API 适配器（微信、支付、地图……）
- 业务特有的缓存、消息队列封装

## 这里**不应该**放什么

以下能力已经由框架 NuGet 包提供，请直接注入接口使用，不要再实现一遍：

| 接口 | 由谁提供 |
|---|---|
| `IPasswordHasher` | `Livia.AspNetCore`（PBKDF2-HMAC-SHA512，带版本化哈希与透明升级） |
| `IJwtTokenService` | `Livia.AspNetCore` |
| `IRefreshTokenStore` | `Livia.AspNetCore`（默认进程内实现；多实例请替换为 Redis/数据库实现） |
| `ICurrentUser` | `Livia.AspNetCore`（从 JWT Claims 解析） |
| `IHttpClientService` | `Livia.AspNetCore`（具名客户端，配置段 `HttpClients`） |

若需替换框架默认实现，直接注册自己的实现即可覆盖（框架内部使用 `TryAdd*` 注册）：

```csharp
// 例如把 Refresh Token 换成 Redis 存储
services.AddSingleton<IRefreshTokenStore, RedisRefreshTokenStore>();
```
