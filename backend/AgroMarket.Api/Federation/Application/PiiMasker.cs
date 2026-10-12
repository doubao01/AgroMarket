using System.Text.RegularExpressions;

namespace AgroMarket.Api.Federation.Application;

/// <summary>
/// 出站数据脱敏: 任何上报/导出的自由文本都要经过这里, 去除手机号、身份证、邮箱、地址门牌等可识别信息。
/// 结构化字段 (金额/数量/品类/区域编码) 不在脱敏范围。
/// </summary>
public static partial class PiiMasker
{
    [GeneratedRegex(@"1[3-9]\d{9}")]
    private static partial Regex MobileRegex();

    [GeneratedRegex(@"\d{17}[\dXx]")]
    private static partial Regex IdCardRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    /// <summary>对自由文本脱敏; null/空返回 null。</summary>
    public static string? Mask(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var s = text;
        s = MobileRegex().Replace(s, m => m.Value[..3] + "****" + m.Value[^4..]);
        s = IdCardRegex().Replace(s, m => m.Value[..6] + "********" + m.Value[^4..]);
        s = EmailRegex().Replace(s, "***");
        return s;
    }
}
