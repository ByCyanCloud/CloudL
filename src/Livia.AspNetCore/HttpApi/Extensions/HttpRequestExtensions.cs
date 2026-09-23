using System.Text;
using Microsoft.AspNetCore.Http;

namespace Livia.AspNetCore.HttpApi.Extensions;

/// <summary>
/// <see cref="HttpRequest"/> 扩展方法。
/// </summary>
public static class HttpRequestExtensions
{
    /// <summary>
    /// 读取请求体文本（需先启用 <c>EnableBuffering</c>）。读取后流位置会复位，可反复读取。
    /// 流不可定位（未启用缓冲）时返回空字符串。
    /// </summary>
    public static async Task<string> ReadBodyAsync(this HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Body.CanSeek)
            return string.Empty;

        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
        request.Body.Position = 0;
        return body;
    }
}
