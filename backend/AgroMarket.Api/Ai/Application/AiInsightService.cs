using AgroMarket.Api.Ai.Domain;
using AgroMarket.Api.Ai.Dtos;
using AgroMarket.Api.Ai.Infrastructure;
using AgroMarket.Api.Ai.Tools;
using AgroMarket.Api.Services;

namespace AgroMarket.Api.Ai.Application;

/// <summary>大屏数据智能解读: 基于实时统计快照生成结论; 问答时数字必须来自工具, 防幻觉。</summary>
public class AiInsightService
{
    private const int MaxQuestionLength = 2000;
    private const int AskRateLimitPerMinute = 20;
    private const int InsightRateLimitPerMinute = 10;

    private readonly ILlmClient _llm;
    private readonly DashboardService _dashboard;
    private readonly AiToolRegistry _tools;
    private readonly AiRateLimiter _limiter;

    public AiInsightService(ILlmClient llm, DashboardService dashboard, AiToolRegistry tools, AiRateLimiter limiter)
    {
        _llm = llm;
        _dashboard = dashboard;
        _tools = tools;
        _limiter = limiter;
    }

    private const string InsightPrompt =
        "你是农产品交易平台的数据分析师。根据提供的大屏统计快照, 用中文生成简短解读。\n" +
        "输出 JSON: summary(一句话总体结论, 60字内), highlights(3条要点数组, 每条40字内, 必须引用具体数字)。\n" +
        "只依据快照数据, 不得编造; 不得给出投资建议。";

    public async Task<AiInsightResponse> InsightAsync(long adminId, CancellationToken ct = default)
    {
        _limiter.Check(adminId, InsightRateLimitPerMinute);

        var overview = await _dashboard.GetOverviewAsync(adminId);
        var trend = await _dashboard.GetTrendAsync(adminId, "7d");
        var region = await _dashboard.GetRegionAsync(adminId);

        var snapshot = System.Text.Json.JsonSerializer.Serialize(new
        {
            overview,
            trend7d = trend,
            regions = region,
        });

        var resp = await _llm.CompleteAsync(
            new List<ChatMessage> { ChatMessage.System(InsightPrompt), ChatMessage.User(snapshot) },
            null, ct);

        return ParseInsight(resp.Content);
    }

    private static AiInsightResponse ParseInsight(string content)
    {
        var json = AiAssistService.ExtractJson(content);
        if (json is null)
            return new AiInsightResponse(content.Trim(), new List<string>());
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
            var highlights = new List<string>();
            if (root.TryGetProperty("highlights", out var h) && h.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var item in h.EnumerateArray())
                    if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                        highlights.Add(item.GetString()!);
            return new AiInsightResponse(summary, highlights);
        }
        catch
        {
            return new AiInsightResponse(content.Trim(), new List<string>());
        }
    }

    private const string AskPrompt =
        "你是农产品交易平台的数据问答助手。用户会问关于平台数据的问题。\n" +
        "你可以调用工具获取真实数据。数字必须来自工具结果, 不得凭记忆编造。\n" +
        "回答简洁(120字内), 若工具未覆盖该问题, 明确说明暂无该维度数据。";

    public async Task<AiAskResponse> AskAsync(long adminId, AiAskRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Question))
            throw new Common.BizException("问题不能为空");
        AiRateLimiter.EnsureLength(req.Question, MaxQuestionLength, "问题");
        _limiter.Check(adminId, AskRateLimitPerMinute);

        var messages = new List<ChatMessage>
        {
            ChatMessage.System(AskPrompt),
            ChatMessage.User(req.Question),
        };

        var facts = new List<string>();
        string answer = "";
        for (var round = 0; round <= 3; round++)
        {
            var resp = await _llm.CompleteAsync(messages, _tools.Definitions, ct);
            if (!resp.HasToolCalls)
            {
                answer = resp.Content;
                break;
            }
            messages.Add(ChatMessage.Assistant(resp.Content, resp.ToolCalls));
            foreach (var call in resp.ToolCalls)
            {
                string result;
                if (_tools.TryGet(call.Name, out var tool))
                {
                    try { result = await tool.InvokeAsync(adminId, call.ArgumentsJson, ct); }
                    catch (Exception ex) { result = $"{{\"error\":\"{ex.Message}\"}}"; }
                    facts.Add($"{call.Name}: {result}");
                }
                else result = "{\"error\":\"未知工具\"}";
                messages.Add(ChatMessage.Tool(call.Id, call.Name, result));
            }
        }

        if (string.IsNullOrWhiteSpace(answer))
            answer = "抱歉, 暂时无法回答该问题。";
        return new AiAskResponse(answer, facts);
    }
}
