using CloudL.Domain.Shared.Constants;

namespace CloudL.AspNetCore.HttpApi.Extensions;

/// <summary>
/// 统一 API 响应包装。
/// </summary>
/// <typeparam name="T">数据类型。</typeparam>
public class ApiResponse<T>
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>业务状态码（四位业务码，成功为 <see cref="ErrorCodes.Success"/>）。</summary>
    public int StatusCode { get; set; }

    /// <summary>提示消息。</summary>
    public string? Message { get; set; }

    /// <summary>响应数据。</summary>
    public T? Data { get; set; }

    /// <summary>字段级验证错误。</summary>
    public Dictionary<string, string[]>? Errors { get; set; }

    /// <summary>错误详情（仅开发环境返回）。</summary>
    public string? ErrorDetail { get; set; }

    /// <summary>错误追踪 ID，用于与服务端日志关联。</summary>
    public string? ErrorId { get; set; }

    /// <summary>构造成功响应。</summary>
    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        StatusCode = ErrorCodes.Success,
        Message = message ?? "操作成功",
        Data = data
    };

    /// <summary>构造创建成功响应。</summary>
    public static ApiResponse<T> Created(T data, string? message = null) => new()
    {
        Success = true,
        StatusCode = ErrorCodes.Success,
        Message = message ?? "创建成功",
        Data = data
    };

    /// <summary>构造失败响应。</summary>
    public static ApiResponse<T> Fail(
        int statusCode,
        string message,
        string? errorDetail = null,
        Dictionary<string, string[]>? errors = null,
        string? errorId = null) => new()
    {
        Success = false,
        StatusCode = statusCode,
        Message = message,
        ErrorDetail = errorDetail,
        Errors = errors,
        ErrorId = errorId ?? Guid.NewGuid().ToString("N")
    };
}

/// <summary>
/// 无数据体的统一 API 响应。
/// </summary>
public class ApiResponse
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>业务状态码。</summary>
    public int StatusCode { get; set; }

    /// <summary>提示消息。</summary>
    public string? Message { get; set; }

    /// <summary>字段级验证错误。</summary>
    public Dictionary<string, string[]>? Errors { get; set; }

    /// <summary>错误详情（仅开发环境返回）。</summary>
    public string? ErrorDetail { get; set; }

    /// <summary>错误追踪 ID。</summary>
    public string? ErrorId { get; set; }

    /// <summary>构造成功响应。</summary>
    public static ApiResponse Ok(string? message = null) => new()
    {
        Success = true,
        StatusCode = ErrorCodes.Success,
        Message = message ?? "操作成功"
    };

    /// <summary>构造创建成功响应。</summary>
    public static ApiResponse Created(string? message = null) => new()
    {
        Success = true,
        StatusCode = ErrorCodes.Success,
        Message = message ?? "创建成功"
    };

    /// <summary>构造失败响应。</summary>
    public static ApiResponse Fail(
        int statusCode,
        string message,
        string? errorDetail = null,
        Dictionary<string, string[]>? errors = null,
        string? errorId = null) => new()
    {
        Success = false,
        StatusCode = statusCode,
        Message = message,
        ErrorDetail = errorDetail,
        Errors = errors,
        ErrorId = errorId ?? Guid.NewGuid().ToString("N")
    };
}