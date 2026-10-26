using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AgroMarket.Api.Federation.Application;

/// <summary>
/// 数据脱敏: 出站自由文本与入站 JSON 明细都要经过这里, 去除手机号、身份证、邮箱等可识别信息。
/// 不要把脱敏只做在生产端 —— 接收端信任对端载荷等于把 PII 泄漏的开关交给了每一枚节点令牌。
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

    /// <summary>
    /// 递归脱敏 JSON 中的所有字符串值。
    /// 入站必须调用: 脱敏只做在生产端等于无条件信任对端, 而节点令牌有效期一年且随配置文件流转,
    /// 任何一枚泄露的令牌都能把明文手机号/身份证写进本节点库, 并在 ForwardReceived 时向上一级继续扩散。
    /// </summary>
    public static JsonElement MaskData(JsonElement data)
    {
        if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return data;
        var node = TryParse(data.GetRawText());
        if (node is null) return data;
        MaskNode(node);
        return JsonSerializer.SerializeToElement(node);
    }

    /// <summary>脱敏整包 JSON 文本(用于落库的原始载荷); 无法解析时返回 null, 宁可丢诊断信息也不留明文。</summary>
    public static string? MaskJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        var node = TryParse(json);
        if (node is null) return null;
        MaskNode(node);
        return node.ToJsonString();
    }

    private static JsonNode? TryParse(string json)
    {
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return null; }
    }

    private static void MaskNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    var child = obj[key];
                    if (child is null) continue;
                    if (child is JsonValue v && v.TryGetValue<string>(out var s)) obj[key] = Mask(s);
                    else MaskNode(child);
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var child = arr[i];
                    if (child is null) continue;
                    if (child is JsonValue v && v.TryGetValue<string>(out var s)) arr[i] = Mask(s);
                    else MaskNode(child);
                }
                break;
        }
    }
}