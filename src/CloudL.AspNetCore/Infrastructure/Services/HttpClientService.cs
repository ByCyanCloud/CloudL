using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CloudL.Application.Contracts.IServices;
using CloudL.AspNetCore.Configuration;
using CloudL.Domain.Shared.Constants;
using CloudL.Domain.Shared.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudL.AspNetCore.Infrastructure.Services;

/// <summary>
/// 第三方 HTTP 调用服务实现，基于 <c>IHttpClientFactory</c> 具名客户端。
/// <para>失败语义：下游不可达 / 返回非 2xx → <see cref="DownstreamServiceException"/>（HTTP 502）；
/// 超时 → <see cref="DownstreamTimeoutException"/>（HTTP 504）。
/// 第三方响应内容只写入日志，<strong>不会</strong>回传给客户端。</para>
/// </summary>
public class HttpClientService : IHttpClientService
{
    private const int MaxBodyPreviewLength = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<HttpClientEntryOptions> _clientOptions;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<HttpClientService> _logger;

    public HttpClientService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<HttpClientEntryOptions> clientOptions,
        IHttpContextAccessor httpContextAccessor,
        ILogger<HttpClientService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(clientOptions);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _clientOptions = clientOptions;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<TResponse?> GetAsync<TResponse>(
        string url,
        string? accessToken = null,
        string clientName = IHttpClientService.DefaultClientName,
        CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Get, url, null, accessToken, clientName, cancellationToken);

    /// <inheritdoc />
    public async Task<string?> GetStringAsync(
        string url,
        string? accessToken = null,
        string clientName = IHttpClientService.DefaultClientName,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(HttpMethod.Get, url, null, accessToken, clientName, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<TResponse?> PostAsync<TRequest, TResponse>(
        string url,
        TRequest? request,
        string? accessToken = null,
        string clientName = IHttpClientService.DefaultClientName,
        CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Post, url, request, accessToken, clientName, cancellationToken);

    /// <inheritdoc />
    public Task<TResponse?> PutAsync<TRequest, TResponse>(
        string url,
        TRequest? request,
        string? accessToken = null,
        string clientName = IHttpClientService.DefaultClientName,
        CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Put, url, request, accessToken, clientName, cancellationToken);

    /// <inheritdoc />
    public Task<TResponse?> DeleteAsync<TResponse>(
        string url,
        string? accessToken = null,
        string clientName = IHttpClientService.DefaultClientName,
        CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Delete, url, null, accessToken, clientName, cancellationToken);

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string url,
        object? request,
        string? accessToken,
        string clientName,
        CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, url, request, accessToken, clientName, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (typeof(TResponse) == typeof(string))
            return (TResponse)(object)body;

        if (string.IsNullOrWhiteSpace(body))
            return default;

        try
        {
            return JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new DownstreamServiceException(
                ErrorCodes.BadGateway,
                $"第三方服务（{clientName}）响应无法解析",
                $"URL: {url}; Body: {Truncate(body)}",
                ex);
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method,
        string url,
        object? request,
        string? accessToken,
        string clientName,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(clientName);

        using var httpRequest = new HttpRequestMessage(method, url);
        ApplyAuthorization(httpRequest, accessToken, clientName);

        if (request is not null)
        {
            httpRequest.Content = JsonContent.Create(request, options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownstreamTimeoutException(
                ErrorCodes.GatewayTimeout,
                $"第三方服务（{clientName}）请求超时",
                $"URL: {url}; Timeout: {client.Timeout}",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new DownstreamServiceException(
                ErrorCodes.BadGateway,
                $"第三方服务（{clientName}）不可达",
                $"URL: {url}; {ex.Message}",
                ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogWarning(
                "\n第三方请求失败: {ClientName} {Method} {Url} -> {StatusCode} {ReasonPhrase}\n{Body}",
                clientName, method, url, (int)response.StatusCode, response.ReasonPhrase, Truncate(responseBody));

            var statusCode = (int)response.StatusCode;
            var reasonPhrase = response.ReasonPhrase;
            response.Dispose();

            throw new DownstreamServiceException(
                ErrorCodes.BadGateway,
                $"第三方服务（{clientName}）返回失败状态",
                $"HTTP {statusCode} {reasonPhrase}; Body: {Truncate(responseBody)}");
        }

        return response;
    }

    private void ApplyAuthorization(HttpRequestMessage request, string? accessToken, string clientName)
    {
        var token = ResolveAccessToken(accessToken, clientName);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private string? ResolveAccessToken(string? accessToken, string clientName)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
            return accessToken;

        var options = _clientOptions.Get(clientName);
        if (!options.ForwardAuthorization)
            return null;

        var authHeader = _httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
            return null;

        return authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..].Trim()
            : authHeader;
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Length <= MaxBodyPreviewLength
            ? value
            : value[..MaxBodyPreviewLength] + "...";
    }
}
