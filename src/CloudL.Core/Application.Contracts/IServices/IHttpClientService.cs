namespace CloudL.Application.Contracts.IServices;

/// <summary>
/// 第三方 HTTP 调用服务。基于 <c>IHttpClientFactory</c> 具名客户端封装，
/// 客户端定义在配置段 <c>HttpClients</c> 下（每个第三方服务一个子节）。
/// </summary>
public interface IHttpClientService
{
    /// <summary>默认具名客户端名称；调用方法未指定 clientName 时使用。</summary>
    const string DefaultClientName = "Default";

    /// <summary>GET 请求并反序列化为 <typeparamref name="TResponse"/>（为 string 时返回原始响应体）。</summary>
    Task<TResponse?> GetAsync<TResponse>(
        string url,
        string? accessToken = null,
        string clientName = DefaultClientName,
        CancellationToken cancellationToken = default);

    /// <summary>GET 请求，返回原始响应体字符串。</summary>
    Task<string?> GetStringAsync(
        string url,
        string? accessToken = null,
        string clientName = DefaultClientName,
        CancellationToken cancellationToken = default);

    /// <summary>POST 请求并反序列化为 <typeparamref name="TResponse"/>（无请求体时传 null）。</summary>
    Task<TResponse?> PostAsync<TRequest, TResponse>(
        string url,
        TRequest? request,
        string? accessToken = null,
        string clientName = DefaultClientName,
        CancellationToken cancellationToken = default);

    /// <summary>PUT 请求并反序列化为 <typeparamref name="TResponse"/>。</summary>
    Task<TResponse?> PutAsync<TRequest, TResponse>(
        string url,
        TRequest? request,
        string? accessToken = null,
        string clientName = DefaultClientName,
        CancellationToken cancellationToken = default);

    /// <summary>DELETE 请求并反序列化为 <typeparamref name="TResponse"/>。</summary>
    Task<TResponse?> DeleteAsync<TResponse>(
        string url,
        string? accessToken = null,
        string clientName = DefaultClientName,
        CancellationToken cancellationToken = default);
}