using CloudL.AspNetCore.Configuration;
using Microsoft.Extensions.Options;

namespace CloudL.AspNetCore.Logging;

/// <summary>
/// <see cref="ISensitiveDataLogPolicy"/> 的默认实现：读一次配置，之后只读。
/// </summary>
/// <remarks>
/// <para>用 <see cref="IOptions{T}"/> 而不是 <c>IOptionsMonitor</c>：开关在<strong>启动期</strong>定型，
/// 因此装配阶段（如 <c>UseCloudLRequestLogging</c>）解析一次即可安全捕获，
/// 不必每个请求都去 DI 里取 —— 也不会出现"同一进程里前后两个请求用了不同开关"的情形。</para>
/// </remarks>
public sealed class SensitiveDataLogPolicy : ISensitiveDataLogPolicy
{
    private readonly bool _revealSensitiveData;

    /// <summary>
    /// 未注入策略时的默认策略（明文，与 <c>Logging:RevealSensitiveDataInLogs</c> 不配置时的默认值一致）。
    /// </summary>
    /// <remarks>
    /// <para><strong>为什么可以是一个静态属性</strong>：它是<strong>不可变单例</strong> —— 构造完成后只有一个
    /// <c>readonly bool</c>，没有任何写入口，也不随请求/配置变化，因此不存在"隐藏的全局可变状态"与竞态
    /// （CLR 保证静态初始化只执行一次）。</para>
    /// <para>存在的唯一理由：<see cref="CloudL.AspNetCore.HttpApi.Filters.ValidationFilter"/> 与
    /// <c>ExceptionHandlingMiddleware</c> 为了<strong>二进制兼容</strong>保留了不接收策略的旧公开构造
    /// （已发布包的调用方仍然能编译/运行），手工走那两个构造时用本默认值 ——
    /// 与"不配置该键即明文"的契约保持一致。DI 激活时走的是带策略的构造，不经过这里。</para>
    /// </remarks>
    public static ISensitiveDataLogPolicy Default { get; } =
        new SensitiveDataLogPolicy(SensitiveDataLoggingOptions.DefaultRevealSensitiveDataInLogs);

    /// <summary>由配置绑定构造（DI 使用）。</summary>
    public SensitiveDataLogPolicy(IOptions<SensitiveDataLoggingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _revealSensitiveData = options.Value.RevealSensitiveDataInLogs;
    }

    /// <summary>
    /// 直接指定模式构造。<strong>故意保持私有</strong>：DI 激活时容器只会看到<see cref="SensitiveDataLogPolicy(IOptions{SensitiveDataLoggingOptions})"/>
    /// 一个公开构造，不存在"多个可解析构造"的歧义；需要指定模式的测试/手工装配走
    /// <c>Options.Create(new SensitiveDataLoggingOptions { RevealSensitiveDataInLogs = … })</c>。
    /// </summary>
    private SensitiveDataLogPolicy(bool revealSensitiveData) => _revealSensitiveData = revealSensitiveData;

    /// <inheritdoc />
    public bool RevealSensitiveData => _revealSensitiveData;

    /// <inheritdoc />
    public string FormatAuthorizationHeader(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
            return SensitiveDataRedactor.NonePlaceholder;

        return _revealSensitiveData
            ? authorizationHeader.Trim()
            : SensitiveDataRedactor.RedactAuthorizationHeader(authorizationHeader);
    }

    /// <inheritdoc />
    public string FormatBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SensitiveDataRedactor.NonePlaceholder;

        if (!_revealSensitiveData)
            return SensitiveDataRedactor.RedactBody(body);

        // 明文模式：内容原样输出（不再按键名/值的形态遮蔽），但仍保留两项与脱敏模式一致的护栏 ——
        // 1) 长度上限（否则一个超大请求体就能把日志灌爆）；
        // 2) 两端裁剪（块状日志的消息不得以换行开头/结尾：空行由输出模板的 {NewLine} 产生）。
        var trimmed = body.Trim();

        return trimmed.Length > SensitiveDataRedactor.MaxBodyLength
            ? trimmed[..SensitiveDataRedactor.MaxBodyLength] + SensitiveDataRedactor.TruncationMarker
            : trimmed;
    }

    /// <inheritdoc />
    public string FormatQueryString(string? queryString)
    {
        if (string.IsNullOrWhiteSpace(queryString))
            return string.Empty;

        if (!_revealSensitiveData)
            return SensitiveDataRedactor.RedactQueryString(queryString);

        // 明文模式：原样保留（含开头的 '?'），长度上限与脱敏模式相同。
        var trimmed = queryString.Trim();

        return trimmed.Length > SensitiveDataRedactor.MaxQueryStringLength
            ? trimmed[..SensitiveDataRedactor.MaxQueryStringLength] + SensitiveDataRedactor.TruncationMarker
            : trimmed;
    }
}
