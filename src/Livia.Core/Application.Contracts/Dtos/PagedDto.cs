using System.ComponentModel.DataAnnotations;
using Livia.Domain.Shared.Constants;

namespace Livia.Application.Contracts.Dtos;

/// <summary>
/// 通用分页请求。
/// </summary>
public class PagedRequestDto
{
    /// <summary>页码（从 1 开始）。</summary>
    [Range(1, int.MaxValue, ErrorMessage = "页码必须大于等于 1")]
    public int PageIndex { get; set; } = 1;

    /// <summary>每页条数。</summary>
    [Range(1, AppConstants.MaxPageSize, ErrorMessage = "每页条数必须在 1 到 100 之间")]
    public int PageSize { get; set; } = AppConstants.DefaultPageSize;

    /// <summary>排序字段。各业务服务应使用白名单映射，避免直接拼接为用户输入。</summary>
    public string? SortBy { get; set; }

    /// <summary>排序方向：asc / desc（默认 desc）。</summary>
    public string? SortDirection { get; set; }
}

/// <summary>
/// 通用分页响应。
/// </summary>
/// <typeparam name="T">列表元素类型。</typeparam>
public class PagedResponseDto<T>
{
    /// <summary>当前页数据。</summary>
    public IReadOnlyList<T> Items { get; set; } = [];

    /// <summary>满足条件的总条数。</summary>
    public int TotalCount { get; set; }

    /// <summary>当前页码。</summary>
    public int PageIndex { get; set; }

    /// <summary>每页条数。</summary>
    public int PageSize { get; set; }

    /// <summary>总页数。</summary>
    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling((double)TotalCount / PageSize)
        : 0;
}