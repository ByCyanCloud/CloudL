using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CloudL.AspNetCore.Logging;

/// <summary>
/// 日志脱敏工具。
/// 采用<strong>两层</strong>策略，避免"改了个字段名就漏"的问题：
/// <list type="number">
///   <item>按键名遮蔽：<c>password</c>、<c>refreshToken</c>、<c>client_secret</c>、<c>access_token</c> 等常见敏感字段
///         （比较时会忽略下划线与连字符，因此 <c>access_token</c>、<c>api-key</c> 同样命中）；</item>
///   <item>按值的形态遮蔽：JWT 形状的字符串、以及长串不透明令牌（≥40 位 base64/hex 风格字符），
///         即使键名起成 <c>pwd</c>、<c>pin</c>、<c>ticket</c> 也会被遮住。</item>
/// </list>
/// 非 JSON 请求体整体省略（只保留长度），避免误记敏感内容。
/// </summary>
public static class SensitiveDataRedactor
{
    private const string Mask = "***";
    private const int MaxBodyLength = 4096;

    /// <summary>query 串的日志长度上限（请求体有上限，query 串同样需要）。</summary>
    private const int MaxQueryStringLength = 1024;

    /// <summary>按名遮蔽的字段名（大小写不敏感；比较前会去掉下划线与连字符）。</summary>
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "newpassword",
        "oldpassword",
        "confirmpassword",
        "passwordhash",
        "accesstoken",
        "refreshtoken",
        "token",
        "idtoken",
        "secret",
        "secretkey",
        "clientsecret",
        "apikey",
        "authorization",
        "credential",
        "credentials",
        "signature",
        "pwd",
        "pin"
    };

    /// <summary>JWT 形状：三段 base64url，以 eyJ 开头。</summary>
    private static readonly Regex JwtPattern = new(
        @"^eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>长串不透明令牌：≥40 位 base64/hex 风格字符（不含点、@、空格，避免误伤邮箱与路径）。</summary>
    private static readonly Regex OpaqueTokenPattern = new(
        @"^[A-Za-z0-9_\-+/=]{40,}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>判断某个字段名是否需要遮蔽（忽略大小写、下划线与连字符）。</summary>
    public static bool IsSensitiveKey(string? key) =>
        !string.IsNullOrEmpty(key) && SensitiveKeys.Contains(NormalizeKey(key));

    /// <summary>
    /// 脱敏 query string：保留参数名与普通参数值，遮蔽敏感参数的值。
    /// <para>用于请求日志 —— 否则 <c>?access_token=...</c> 这类令牌会随日志落盘。</para>
    /// </summary>
    public static string RedactQueryString(string? queryString)
    {
        if (string.IsNullOrWhiteSpace(queryString))
            return string.Empty;

        var truncated = queryString.Length > MaxQueryStringLength;
        if (truncated)
            queryString = queryString[..MaxQueryStringLength];

        var hasPrefix = queryString.StartsWith('?');
        var raw = hasPrefix ? queryString[1..] : queryString;

        if (raw.Length == 0)
            return string.Empty;

        var builder = new StringBuilder(raw.Length);

        foreach (var pair in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (builder.Length > 0)
                builder.Append('&');

            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0)
            {
                builder.Append(pair);
                continue;
            }

            var key = pair[..separatorIndex];
            var value = pair[(separatorIndex + 1)..];

            builder.Append(key)
                .Append('=')
                .Append(IsSensitiveKey(key) || LooksLikeSecret(Uri.UnescapeDataString(value)) ? Mask : value);
        }

        var result = builder.ToString();
        if (result.Length == 0)
            return string.Empty;

        var redacted = hasPrefix ? "?" + result : result;
        return truncated ? redacted + "...(已截断)" : redacted;
    }

    /// <summary>
    /// 脱敏请求体。
    /// JSON 体会递归处理；非 JSON 体整体省略（只保留长度），避免误记敏感内容。
    /// </summary>
    public static string RedactBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var trimmed = body.Trim();
        var truncated = false;

        if (trimmed.Length > MaxBodyLength)
        {
            trimmed = trimmed[..MaxBodyLength];
            truncated = true;
        }

        if (trimmed[0] is not ('{' or '['))
            return $"[非 JSON 请求体已省略，长度 {body.Length}]";

        try
        {
            var node = JsonNode.Parse(trimmed);
            if (node is null)
                return "[请求体解析结果为空]";

            RedactNode(node);

            var redacted = node.ToJsonString(RedactedJsonOptions);
            return truncated ? redacted + "...(已截断)" : redacted;
        }
        catch (JsonException)
        {
            // 截断可能破坏 JSON 结构；此时整体省略而不是原样输出
            return "[请求体非合法 JSON 或已被截断，内容已省略]";
        }
    }

    /// <summary>脱敏 Authorization 头：保留认证方案与末尾少量字符，既便于排查又不可复用。</summary>
    public static string RedactAuthorizationHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return "(none)";

        var parts = header.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return Mask;

        var token = parts[1];
        var tail = token.Length > 6 ? token[^6..] : token;
        return $"{parts[0]} ***{tail}";
    }

    private static string NormalizeKey(string key) =>
        key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

    private static void RedactNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToList())
                {
                    if (IsSensitiveKey(property.Key))
                    {
                        jsonObject[property.Key] = Mask;
                        continue;
                    }

                    if (TryMaskString(property.Value, out var masked))
                    {
                        jsonObject[property.Key] = masked;
                        continue;
                    }

                    if (property.Value is not null)
                        RedactNode(property.Value);
                }

                break;

            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    if (TryMaskString(jsonArray[index], out var masked))
                    {
                        jsonArray[index] = masked;
                        continue;
                    }

                    if (jsonArray[index] is not null)
                        RedactNode(jsonArray[index]!);
                }

                break;
        }
    }

    private static bool TryMaskString(JsonNode? node, out string masked)
    {
        masked = Mask;

        if (node is JsonValue value && value.TryGetValue<string>(out var text) && LooksLikeSecret(text))
            return true;

        masked = string.Empty;
        return false;
    }

    /// <summary>按键名兜不住的部分：靠值的形态识别。</summary>
    private static bool LooksLikeSecret(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        return JwtPattern.IsMatch(value) || OpaqueTokenPattern.IsMatch(value);
    }

    /// <summary>序列化脱敏结果时保留中文可读性（默认编码器会把中文转义成 Unicode 转义序列）。</summary>
    private static readonly JsonSerializerOptions RedactedJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
