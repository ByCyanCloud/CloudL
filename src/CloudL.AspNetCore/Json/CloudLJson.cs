using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudL.AspNetCore.Json;

/// <summary>
/// 框架统一的 JSON 约定：snake_case 命名、忽略 null、不转义中文。
/// 中间件与 JWT 事件使用此处的选项，MVC 管道通过 <c>AddCloudLJsonOptions()</c> 应用同一套约定。
/// </summary>
public static class CloudLJson
{
    /// <summary>框架统一序列化选项。</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>按框架约定序列化。</summary>
    public static string Serialize<TValue>(TValue value) =>
        JsonSerializer.Serialize(value, SerializerOptions);
}