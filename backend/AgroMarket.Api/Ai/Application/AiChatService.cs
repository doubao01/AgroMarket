using AgroMarket.Api.Ai.Domain;
using AgroMarket.Api.Ai.Dtos;
using AgroMarket.Api.Ai.Infrastructure;
using AgroMarket.Api.Ai.Tools;
using AgroMarket.Api.Common;
using Microsoft.Extensions.Caching.Memory;

namespace AgroMarket.Api.Ai.Application;

/// <summary>AI 客服助手: 简化 ReAct 循环 (意图 -> 工具调用 -> 回答), 工具轮次有硬上限防失控。</summary>
public class AiChatService
{
    private const int MaxToolRounds = 3;
    private const int MaxInputLength = 2000;
    private const int MaxHistoryMessages = 10;
    private const int RateLimitPerMinute = 15;

    private readonly ILlmClient _llm;
    private readonly AiToolRegistry _tools;
    private readonly IMemoryCache _cache;

    public AiChatService(ILlmClient llm, AiToolRegistry tools, IMemoryCache cache)
    {
        _llm = llm;
        _tools = tools;
        _cache = cache;
    }

    private const string SystemPrompt =
        "你是「息县农产品交易平台」的智能客服助手, 服务于本地农户、合作社与采购商。\n" +
        "职责: 解答平台使用问题(注册/发布/报价/下单/支付/聊天等)、供需对接建议、常见农事与行情常识。\n" +
        "规则:\n" +
        "1. 涉及平台具体数据(供应、需求、订单、成交、趋势)时, 必须调用提供的工具获取, 不得凭记忆编造数字。\n" +
        "2. 不承诺任何交易结果, 不代替用户下单、报价或支付; 涉及资金操作时引导用户到对应页面手动完成。\n" +
        "3. 回答简洁、口语化、分点说明, 单次不超过 200 字。\n" +
        "4. 超出农产品交易与平台范围的问题, 礼貌说明并引导回平台相关话题。";

    public async Task<AiChatResponse> ChatAsync(long userId, AiChatRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            throw new BizException("消息内容不能为空");
        if (req.Message.Length > MaxInputLength)
            throw new BizException($"消息过长 (最多 {MaxInputLength} 字)");

        EnforceRateLimit(userId);

        var sessionId = string.IsNullOrWhiteSpace(req.SessionId) ? Guid.NewGuid().ToString("N") : req.SessionId!;
        var history = GetHistory(sessionId);

        var messages = new List<ChatMessage> { ChatMessage.System(SystemPrompt) };
        messages.AddRange(history);
        messages.Add(ChatMessage.User(req.Message));

        var toolsUsed = new List<string>();
        var totalTokens = 0;
        string reply = "";

        for (var round = 0; round <= MaxToolRounds; round++)
        {
            var resp = await _llm.CompleteAsync(messages, _tools.Definitions, ct);
            totalTokens += resp.TotalTokens;

            if (!resp.HasToolCalls)
            {
                reply = resp.Content;
                break;
            }

            messages.Add(ChatMessage.Assistant(resp.Content));
            foreach (var call in resp.ToolCalls)
            {
                string result;
                if (_tools.TryGet(call.Name, out var tool))
                {
                    toolsUsed.Add(call.Name);
                    try
                    {
                        result = await tool.InvokeAsync(userId, call.ArgumentsJson, ct);
                    }
                    catch (Exception ex)
                    {
                        result = $"{{\"error\":\"{ex.Message}\"}}";
                    }
                }
                else
                {
                    result = "{\"error\":\"未知工具\"}";
                }
                messages.Add(ChatMessage.Tool(result));
            }
        }

        if (string.IsNullOrWhiteSpace(reply))
            reply = "抱歉, 我暂时无法回答这个问题, 请换个说法或咨询客服。";

        history.Add(ChatMessage.User(req.Message));
        history.Add(ChatMessage.Assistant(reply));
        TrimHistory(history);
        SaveHistory(sessionId, history);

        return new AiChatResponse(reply, sessionId, toolsUsed.Distinct().ToList(), totalTokens);
    }

    private void EnforceRateLimit(long userId)
    {
        var key = $"ai_rl_{userId}";
        var count = _cache.GetOrCreate(key, e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return 0;
        });
        if (count >= RateLimitPerMinute)
            throw new BizException("操作过于频繁, 请稍后再试", ErrorCodes.Conflict);
        _cache.Set(key, count + 1, TimeSpan.FromMinutes(1));
    }

    private List<ChatMessage> GetHistory(string sessionId)
        => _cache.Get<List<ChatMessage>>($"ai_hist_{sessionId}") ?? new List<ChatMessage>();

    private void SaveHistory(string sessionId, List<ChatMessage> history)
        => _cache.Set($"ai_hist_{sessionId}", history, TimeSpan.FromHours(2));

    private static void TrimHistory(List<ChatMessage> history)
    {
        // 保留最近 N 条 (成对裁剪, 避免出现孤立 assistant)
        var max = MaxHistoryMessages;
        if (history.Count > max)
            history.RemoveRange(0, history.Count - max);
    }
}
