using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;

namespace CloudL.AspNetCore.HttpApi.Binding;

/// <summary>
/// query 参数键的映射工具：把 snake_case 键映射为模型绑定可识别的 PascalCase 键。
/// <para>约定：query 使用 snake_case（<c>?page_index=2&amp;sort_by=user_name</c>），
/// 与 JSON 请求/响应体、验证错误键保持一致。</para>
/// </summary>
public static class SnakeCaseQueryKey
{
    /// <summary>
    /// 构建值提供器使用的字典：<strong>保留原始键</strong>（兼容既有 camelCase 调用方），
    /// 并为 snake_case / 嵌套（含点）的键追加 PascalCase 别名。
    /// </summary>
    /// <remarks>
    /// 若同时传了 <c>page_index</c> 与 <c>pageIndex</c>，以 snake_case（约定写法）为准，
    /// 保证行为确定而不是依赖参数顺序。
    /// </remarks>
    public static Dictionary<string, StringValues> Build(
        IEnumerable<KeyValuePair<string, StringValues>> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pairs = query as IReadOnlyList<KeyValuePair<string, StringValues>> ?? query.ToList();
        var values = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        // 第一遍：原始键
        foreach (var pair in pairs)
        {
            values[pair.Key] = pair.Value;
        }

        // 第二遍：为 snake_case / 嵌套键追加 PascalCase 别名
        foreach (var pair in pairs)
        {
            var pascalCaseKey = ToPascalCase(pair.Key);
            if (!string.Equals(pascalCaseKey, pair.Key, StringComparison.Ordinal))
            {
                values[pascalCaseKey] = pair.Value;
            }
        }

        return values;
    }

    /// <summary>
    /// 把 snake_case / 嵌套键转换为 PascalCase：
    /// <c>page_index</c> → <c>PageIndex</c>、<c>filter.sort_by</c> → <c>Filter.SortBy</c>。
    /// </summary>
    /// <remarks>
    /// 只在需要改写时处理：含下划线（snake_case）或含点（嵌套模型）。
    /// 纯粹的 camelCase / 单词键原样返回，避免改变既有语义。
    /// </remarks>
    public static string ToPascalCase(string key)
    {
        if (string.IsNullOrEmpty(key) || (!key.Contains('_') && !key.Contains('.')))
            return key;

        var segments = key.Split('.');
        for (var index = 0; index < segments.Length; index++)
        {
            segments[index] = ConvertSegment(segments[index]);
        }

        return string.Join('.', segments);
    }

    /// <summary>
    /// 转换单个段（不含点）：<c>page_index</c> → <c>PageIndex</c>、<c>filter</c> → <c>Filter</c>。
    /// </summary>
    private static string ConvertSegment(string segment)
    {
        if (segment.Length == 0)
            return segment;

        var parts = segment.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return segment;

        var builder = new StringBuilder(segment.Length);
        foreach (var part in parts)
        {
            builder.Append(char.ToUpperInvariant(part[0]));

            if (part.Length > 1)
                builder.Append(part.AsSpan(1));
        }

        return builder.ToString();
    }
}

/// <summary>
/// 支持 snake_case query 参数名的值提供器。
/// </summary>
public sealed class SnakeCaseQueryValueProvider : IValueProvider
{
    private readonly Dictionary<string, StringValues> _values;
    private readonly CultureInfo _culture;

    internal SnakeCaseQueryValueProvider(Dictionary<string, StringValues> values, CultureInfo culture)
    {
        _values = values;
        _culture = culture;
    }

    /// <inheritdoc />
    public bool ContainsPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return _values.Count > 0;

        foreach (var key in _values.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <inheritdoc />
    public ValueProviderResult GetValue(string key) =>
        _values.TryGetValue(key, out var value)
            ? new ValueProviderResult(value, _culture)
            : ValueProviderResult.None;
}

/// <summary>
/// 注册 <see cref="SnakeCaseQueryValueProvider"/> 的工厂。
/// 由 <c>AddCloudLAspNetCore</c> 自动插入 MVC 的值提供器列表首位。
/// </summary>
public sealed class SnakeCaseQueryValueProviderFactory : IValueProviderFactory
{
    /// <inheritdoc />
    public Task CreateValueProviderAsync(ValueProviderFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var query = context.ActionContext.HttpContext.Request.Query;
        if (query.Count == 0)
            return Task.CompletedTask;

        var values = SnakeCaseQueryKey.Build(query);
        context.ValueProviders.Add(new SnakeCaseQueryValueProvider(values, CultureInfo.InvariantCulture));

        return Task.CompletedTask;
    }
}
