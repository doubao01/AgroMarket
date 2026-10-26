using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace AgroMarket.Api.Common;

/// <summary>
/// 带凭据打码的控制台日志格式化器。
/// 起因: 聊天长连接曾用 <c>/ws/chat?token=&lt;jwt&gt;</c> 握手, 而 ASP.NET 默认按 Information 级别
/// 打印完整请求行(含 query), 于是 7 天有效的账号凭据连同其中的手机号 claim 被写进 stdout、
/// 日志采集与反向代理访问日志 —— 读到日志即等于拿到任意账号 7 天的登录态。
/// 协议已改为请求头传 token 并降低了该类别日志级别, 这里再做一层与来源无关的兜底:
/// 任何路径(代理、第三方回调、异常文本)把凭据写进日志, 都会被打码。
/// 布局仿照默认 SimpleConsole, 只是消息先过一遍 Scrub。
/// </summary>
public sealed partial class SecretScrubbingConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "secret-scrubbing";

    public SecretScrubbingConsoleFormatter(IOptionsMonitor<ConsoleFormatterOptions> options) : base(FormatterName)
    {
        _useUtcTimestamp = options.CurrentValue.UseUtcTimestamp;
        _timestampFormat = options.CurrentValue.TimestampFormat ?? "HH:mm:ss ";
    }

    private readonly bool _useUtcTimestamp;
    private readonly string _timestampFormat;

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = Scrub(logEntry.Formatter(logEntry.State, logEntry.Exception));
        var now = _useUtcTimestamp ? DateTime.UtcNow : DateTime.Now;
        textWriter.Write(now.ToString(_timestampFormat));
        textWriter.Write(Level(logEntry.LogLevel));
        textWriter.Write(": ");
        textWriter.Write(logEntry.Category);
        textWriter.Write('[');
        textWriter.Write(logEntry.EventId.Id);
        textWriter.WriteLine(']');
        textWriter.WriteLine("      " + message);
        if (logEntry.Exception != null)
            textWriter.WriteLine(Scrub(logEntry.Exception.ToString()));
    }

    private static string Level(LogLevel level) => level switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        LogLevel.Critical => "crit",
        _ => "none",
    };

    /// <summary>把凭据替换为 ***; 只匹配凭据本身, 不动其它日志内容。</summary>
    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var s = JwtRegex().Replace(text, "***");
        s = BearerRegex().Replace(s, "$1***");
        s = QueryTokenRegex().Replace(s, "$1=***");
        s = PasswordQueryRegex().Replace(s, "$1=***");
        return s;
    }

    // JWT: 三段 base64url
    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*")]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"(?i)\b(bearer\s+)\S+")]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"(?i)\b(token|access_token|auth_token|ticket)=([^&\s""'<>]*)")]
    private static partial Regex QueryTokenRegex();

    [GeneratedRegex(@"(?i)\b(password|passwd|pwd|secret|api_?key)=([^&\s""'<>]*)")]
    private static partial Regex PasswordQueryRegex();
}
