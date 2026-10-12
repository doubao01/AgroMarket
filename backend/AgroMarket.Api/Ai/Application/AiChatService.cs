using AgroMarket.Api.Ai.Domain;
using AgroMarket.Api.Ai.Dtos;
using AgroMarket.Api.Ai.Infrastructure;
using AgroMarket.Api.Ai.Tools;
using AgroMarket.Api.Common;

namespace AgroMarket.Api.Ai.Application;

/// <summary>AI 客服助手: 简化 ReAct 循环 (意图 -> 工具调用 -> 回答), 工具轮次有硬上限防失控。对话历史持久化。</summary>
public class AiChatService
{
    private const int MaxToolRounds = 3;
    private const int MaxInputLength = 2000;
    private const int MaxHistoryMessages = 10;
    private const int RateLimitPerMinute = 15;
    private const int MaxTitleLength = 30;

    private readonly ILlmClient _llm;
    private readonly AiToolRegistry _tools;
    private readonly AiRateLimiter _limiter;
    private readonly AiChatRepository _repo;

    public AiChatService(ILlmClient llm, AiToolRegistry tools, AiRateLimiter limiter, AiChatRepository repo)
    {
        _llm = llm;
        _tools = tools;
        _limiter = limiter;
        _repo = repo;
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
        AiRateLimiter.EnsureLength(req.Message, MaxInputLength, "消息");

        _limiter.Check(userId, RateLimitPerMinute);

        // 延迟建库: 先复用已有会话(校验归属), 新建会话待首轮成功后再落库, 避免 LLM 失败产生空会话。
        AiConversationRow? conv = null;
        var history = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(req.SessionId))
        {
            var existing = await _repo.GetBySessionAsync(req.SessionId.Trim());
            if (existing is not null)
            {
                if (existing.UserId != userId)
                    throw new BizException("无权访问该会话", ErrorCodes.Forbidden);
                conv = existing;
                history = await LoadHistoryAsync(conv.Id);
            }
        }

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

            // 原样回传带 tool_calls 的助手消息, 再逐条以 tool_call_id 关联工具结果 (OpenAI 协议要求)
            messages.Add(ChatMessage.Assistant(resp.Content, resp.ToolCalls));
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
                messages.Add(ChatMessage.Tool(call.Id, call.Name, result));
            }
        }

        if (string.IsNullOrWhiteSpace(reply))
            reply = "抱歉, 我暂时无法回答这个问题, 请换个说法或咨询客服。";

        if (conv is null)
        {
            var sid = string.IsNullOrWhiteSpace(req.SessionId)
                ? Guid.NewGuid().ToString("N")
                : req.SessionId!.Trim();
            conv = await _repo.CreateAsync(userId, sid);
        }

        var distinctTools = toolsUsed.Distinct().ToList();
        var toolsJson = distinctTools.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(distinctTools)
            : null;
        await _repo.AppendTurnAsync(conv.Id, req.Message, reply, toolsJson, totalTokens,
            BuildTitle(req.Message));

        return new AiChatResponse(reply, conv.SessionId, distinctTools, totalTokens);
    }

    // ---------------- 会话管理 ----------------

    /// <summary>会话列表(分页)。</summary>
    public async Task<AiConversationPageDto> ListConversationsAsync(long userId, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;
        var items = await _repo.ListAsync(userId, pageSize, (page - 1) * pageSize);
        var total = await _repo.CountAsync(userId);
        return new AiConversationPageDto(items.Select(AiChatRepository.MapConversation).ToList(), total);
    }

    /// <summary>会话消息分页(游标 beforeId, 取更早消息)。</summary>
    public async Task<AiMessagePageDto> ListMessagesAsync(long userId, long convId, long? beforeId, int limit)
    {
        await EnsureOwnedAsync(userId, convId);
        if (limit is < 1 or > 50) limit = 20;
        var rows = await _repo.ListMessagesAsync(convId, beforeId, limit + 1);
        var hasMore = rows.Count > limit;
        if (hasMore) rows = rows.Take(limit).ToList();
        var items = rows.Select(AiChatRepository.MapMessage).ToList();
        return new AiMessagePageDto(items, hasMore, hasMore && items.Count > 0 ? items[0].Id : null);
    }

    public async Task DeleteConversationAsync(long userId, long convId)
    {
        if (!await _repo.DeleteAsync(convId, userId))
            throw new BizException("会话不存在", ErrorCodes.NotFound);
    }

    public async Task RenameConversationAsync(long userId, long convId, string title)
    {
        await EnsureOwnedAsync(userId, convId);
        title = (title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            throw new BizException("标题不能为空");
        if (title.Length > MaxTitleLength) title = title[..MaxTitleLength];
        await _repo.RenameAsync(convId, userId, title);
    }

    // ---------------- 内部 ----------------

    private async Task EnsureOwnedAsync(long userId, long convId)
    {
        if (await _repo.GetOwnedAsync(convId, userId) is null)
            throw new BizException("会话不存在", ErrorCodes.NotFound);
    }

    private async Task<List<ChatMessage>> LoadHistoryAsync(long convId)
    {
        var rows = await _repo.GetRecentMessagesAsync(convId, MaxHistoryMessages);
        return rows.Select(r => r.Role == "ASSISTANT"
            ? ChatMessage.Assistant(r.Content)
            : ChatMessage.User(r.Content)).ToList();
    }

    private static string BuildTitle(string message)
    {
        var t = (message ?? "").Trim().Replace('\n', ' ');
        if (t.Length > MaxTitleLength) t = t[..MaxTitleLength];
        return string.IsNullOrWhiteSpace(t) ? "新对话" : t;
    }
}
