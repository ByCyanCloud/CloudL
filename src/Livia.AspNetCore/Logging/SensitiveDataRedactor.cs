using System.Text.Json;
using System.Text.Json.Nodes;

namespace Livia.AspNetCore.Logging;

/// <summary>
/// 日志脱敏工具。
/// 用于在记录请求体与认证头时遮蔽口令、令牌等敏感信息 —— 明文凭据绝不应进入日志文件。
/// </summary>
public static class SensitiveDataRedactor
{
    private const string Mask = "\"***\"";
    private const int MaxBodyLength = 4096;

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
        "signature"
    };

    /// <summary>
    /// 脱敏请求体。
    /// JSON 体会按键名递归遮蔽敏感字段；非 JSON 体会整体省略（只保留长度），避免误记敏感内容。
    /// </summary>
    public static string RedactBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var trimmed = body.Trim();
        if (trimmed.Length > MaxBodyLength)
        {
            trimmed = trimmed[..MaxBodyLength];
        }

        if (trimmed[0] is not ('{' or '['))
            return $"[非 JSON 请求体已省略，长度 {body.Length}]";

        try
        {
            var node = JsonNode.Parse(trimmed);
            if (node is null)
                return "[请求体解析结果为空]";

            RedactNode(node);
            return node.ToJsonString();
        }
        catch (JsonException)
        {
            return "[请求体非合法 JSON，内容已省略]";
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

    private static void RedactNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToList())
                {
                    if (SensitiveKeys.Contains(property.Key))
                    {
                        jsonObject[property.Key] = Mask;
                    }
                    else if (property.Value is not null)
                    {
                        RedactNode(property.Value);
                    }
                }

                break;

            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    if (item is not null)
                        RedactNode(item);
                }

                break;
        }
    }
}