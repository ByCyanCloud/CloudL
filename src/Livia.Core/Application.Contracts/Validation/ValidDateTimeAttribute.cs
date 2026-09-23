using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Livia.Application.Contracts.Validation;

/// <summary>
/// 校验日期时间字段格式是否有效。可空字段的 null 视为合法。
/// 用法：<c>[ValidDateTime(AllowedFormats = ["yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd"])]</c>
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class ValidDateTimeAttribute : ValidationAttribute
{
    /// <summary>允许的日期时间格式列表（作为 DateTime.TryParseExact 的格式）。为空时使用宽松解析。</summary>
    public string[]? AllowedFormats { get; set; }

    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
            return ValidationResult.Success;

        if (value is DateTime or DateTimeOffset)
            return ValidationResult.Success;

        if (value is string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return ValidationResult.Success;

            var parsed = AllowedFormats is { Length: > 0 }
                ? DateTime.TryParseExact(str, AllowedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                : DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

            if (parsed)
                return ValidationResult.Success;

            return new ValidationResult(
                ErrorMessage ?? $"'{validationContext.DisplayName}' 不是有效的日期时间格式。示例：2026-12-01 或 2026-12-01T10:30:00");
        }

        return ValidationResult.Success;
    }
}