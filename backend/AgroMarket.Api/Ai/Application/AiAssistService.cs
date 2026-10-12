using System.Text.Json;
using AgroMarket.Api.Ai.Domain;
using AgroMarket.Api.Ai.Dtos;
using AgroMarket.Api.Ai.Infrastructure;
using AgroMarket.Api.Common;

namespace AgroMarket.Api.Ai.Application;

/// <summary>内容发布辅助: 生成描述/分类建议/价格区间。结果仅供表单预填, 不自动提交。</summary>
public class AiAssistService
{
    private const int MaxInputLength = 2000;
    private const int RateLimitPerMinute = 15;

    private readonly ILlmClient _llm;
    private readonly AiRateLimiter _limiter;

    public AiAssistService(ILlmClient llm, AiRateLimiter limiter)
    {
        _llm = llm;
        _limiter = limiter;
    }

    public async Task<AiAssistResponse> AssistPublishAsync(long userId, AiAssistRequest req, CancellationToken ct = default)
    {
        if (req.Type != "SUPPLY" && req.Type != "DEMAND")
            throw new BizException("类型必须为 SUPPLY 或 DEMAND");

        var combined = $"{(req.Title ?? "")}{(req.Category ?? "")}{(req.Keywords ?? "")}";
        AiRateLimiter.EnsureLength(combined, MaxInputLength, "输入内容");
        _limiter.Check(userId, RateLimitPerMinute);

        var kind = req.Type == "SUPPLY" ? "供应(出售)" : "需求(采购)";
        var prompt =
            $"你是农产品交易文案助手。请为一条{kind}信息生成规范内容。\n" +
            $"已知信息: 标题={req.Title ?? "无"}; 分类={req.Category ?? "无"}; 关键词={req.Keywords ?? "无"}; " +
            $"数量={req.Quantity?.ToString() ?? "无"}; 参考价={req.PriceHint?.ToString() ?? "无"}。\n" +
            "请只输出 JSON, 字段: description(80字内中文描述, 无营销夸大), suggestedCategory(品类名), " +
            "priceMin(number, 可空), priceMax(number, 可空), priceUnit(string, 如 元/斤), " +
            "titleSuggestions(3个候选标题数组, 每个20字内, 含品类与数量)。" +
            "价格区间仅作参考, 无把握时 priceMin/priceMax 返回 null。";

        var resp = await _llm.CompleteAsync(
            new List<ChatMessage> { ChatMessage.System(prompt), ChatMessage.User("请生成。") },
            null, ct);

        return ParseAssist(resp.Content);
    }

    private static AiAssistResponse ParseAssist(string content)
    {
        var json = ExtractJson(content);
        if (json is null)
            return new AiAssistResponse(content.Trim(), "", null, null, null, new List<string>());
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? GetStr(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            decimal? GetNum(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;
            var titles = new List<string>();
            if (root.TryGetProperty("titleSuggestions", out var t) && t.ValueKind == JsonValueKind.Array)
                foreach (var item in t.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String)
                        titles.Add(item.GetString()!);
            return new AiAssistResponse(
                GetStr("description") ?? "",
                GetStr("suggestedCategory") ?? "",
                GetNum("priceMin"),
                GetNum("priceMax"),
                GetStr("priceUnit"),
                titles);
        }
        catch
        {
            return new AiAssistResponse(content.Trim(), "", null, null, null, new List<string>());
        }
    }

    internal static string? ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return text.Substring(start, end - start + 1);
    }
}
