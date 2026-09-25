namespace CloudL.Domain.Repositories;

/// <summary>
/// 分页查询结果（领域层载体，与 API 层的分页 DTO 解耦）。
/// </summary>
/// <typeparam name="T">元素类型。</typeparam>
public class PagedResult<T>
{
    /// <summary>当前页数据。</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>满足条件的总条数。</summary>
    public int TotalCount { get; init; }

    /// <summary>当前页码（从 1 开始）。</summary>
    public int PageIndex { get; init; }

    /// <summary>每页条数。</summary>
    public int PageSize { get; init; }
}
