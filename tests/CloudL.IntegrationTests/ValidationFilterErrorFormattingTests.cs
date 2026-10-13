using CloudL.AspNetCore.HttpApi.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CloudL.IntegrationTests;

/// <summary>
/// 受测契约：验证失败日志 <c>[Response]</c> 行里 errors 明细的<strong>两条防护边界</strong> ——
/// 最多列 30 个字段（超出必须标注实际项数 <c>…（共 N 项）</c>）、总长超 1000 字符必须截断并标注。
/// </summary>
/// <remarks>
/// 为什么直接喂 <see cref="ModelStateDictionary"/>：这两条边界要造 40 个失败字段 / 上千字符的消息，
/// 用实体属性或真实请求去凑既慢又脆。这里只钉"格式化结果"的边界；
/// "结果是否真的拼进日志模板、响应是否一条不少"由
/// <see cref="ValidationFilterLoggingTests"/> 的端到端用例守住。
/// </remarks>
public sealed class ValidationFilterErrorFormattingTests
{
    /// <summary>日志里最多列出的失败字段数（与 <c>ValidationFilter.MaxLoggedErrorFields</c> 一致，故意写死以便改上限时立刻失败）。</summary>
    private const int ExpectedFieldCap = 30;

    /// <summary>日志明细的长度上限（字符）。</summary>
    private const int ExpectedLengthCap = 1000;

    [Fact]
    public void OverFieldCap_ShouldListExactlyThirtyFieldsAndTotalCount()
    {
        var modelState = BuildFailedFields(40, "字段必填");

        var errors = ValidationFilter.FormatErrorsForLog(modelState);

        // 恰好列 30 个：少列会让"共 40 项"变得无意义，多列则说明上限没生效。
        Assert.Equal(ExpectedFieldCap, CountOccurrences(errors, "字段必填"));
        Assert.Equal(ExpectedFieldCap, CountOccurrences(errors, "; ") + 1);

        // 关键：必须标注实际失败字段总数，否则排查时会把"列出的 30 个"误当成全部。
        Assert.Contains("…（共 40 项）", errors, StringComparison.Ordinal);

        // 30 条短消息远不到 1000 字符：命中字段上限时不应同时出现长度截断标注。
        Assert.DoesNotContain("…（已截断", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void AtFieldCap_ShouldListEveryFieldWithoutMarker()
    {
        // 边界值本身（正好 30）不能触发截断标注 —— 否则"恰好 30 个字段"会白丢信息。
        var modelState = BuildFailedFields(ExpectedFieldCap, "字段必填");

        var errors = ValidationFilter.FormatErrorsForLog(modelState);

        Assert.Equal(ExpectedFieldCap, CountOccurrences(errors, "字段必填"));
        Assert.DoesNotContain("…（共", errors, StringComparison.Ordinal);
        Assert.DoesNotContain("…（已截断", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void OverLengthCap_ShouldTruncateAndMarkDroppedCharacters()
    {
        const int messageLength = 500;
        var message = new string('长', messageLength);
        var modelState = BuildFailedFields(3, message, namePrefix: "");

        var errors = ValidationFilter.FormatErrorsForLog(modelState);

        // 独立算出期望值：3 条"字段名: 500 字"用 "; " 连接，取前 1000 字符 + 丢弃字符数标注。
        var full = string.Join(
            "; ",
            new[] { "alpha", "bravo", "charlie" }.Select(name => $"{name}: {message}"));
        var expected = string.Concat(
            full.AsSpan(0, ExpectedLengthCap),
            $"…（已截断 {full.Length - ExpectedLengthCap} 字符）");

        Assert.Equal(expected, errors);

        // 兜底：无论怎么截，都不能把整条日志撑爆。
        Assert.True(
            errors.Length < ExpectedLengthCap + 40,
            $"截断后长度异常：{errors.Length}");
    }

    /// <summary>造 <paramref name="count"/> 个失败字段（键名 <c>alpha</c>、<c>bravo</c>… 或带前缀的编号）。</summary>
    private static ModelStateDictionary BuildFailedFields(int count, string message, string namePrefix = "field_")
    {
        var names = new[] { "alpha", "bravo", "charlie" };
        var modelState = new ModelStateDictionary();

        for (var index = 0; index < count; index++)
        {
            var name = namePrefix.Length == 0
                ? names[index % names.Length]
                : $"{namePrefix}{index + 1:D2}";

            modelState.AddModelError(name, message);
        }

        return modelState;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
