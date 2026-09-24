using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudL.AspNetCore.Json;

/// <summary>
/// 框架统一的 JSON 约定：snake_case 属性名、忽略 null、不转义中文。
/// 中间件与 JWT 事件使用此处的选项，MVC 管道通过 <c>AddCloudLJsonOptions()</c> 应用同一套约定。
/// </summary>
/// <remarks>
/// <strong>刻意不设置 <c>DictionaryKeyPolicy</c></strong>：该设置会改写响应中所有字典的键，
/// 包括业务数据的键（例如把 <c>USD</c> 改成 <c>usd</c>），属于语义损坏。
/// 需要 snake_case 的字典（如验证错误键）由产生它的组件自己负责转换。
/// </remarks>
public static class CloudLJson
{
    /// <summary>框架统一序列化选项。</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>按框架约定序列化。</summary>
    public static string Serialize<TValue>(TValue value) =>
        JsonSerializer.Serialize(value, SerializerOptions);
}
