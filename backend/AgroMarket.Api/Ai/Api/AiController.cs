using AgroMarket.Api.Ai.Application;
using AgroMarket.Api.Ai.Dtos;
using AgroMarket.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Ai.Api;

/// <summary>AI 能力接口: 客服助手、发布辅助、数据解读。未配置 LLM 时相关接口返回 501。</summary>
[ApiController]
[Route("api/ai")]
[RequireAuth]
public class AiController : ControllerBase
{
    private readonly AiChatService _chat;
    private readonly AiAssistService _assist;
    private readonly AiInsightService _insight;

    public AiController(AiChatService chat, AiAssistService assist, AiInsightService insight)
    {
        _chat = chat;
        _assist = assist;
        _insight = insight;
    }

    private long Uid => HttpContext.User().Id;
    private static bool IsAdmin(HttpContext ctx) => ctx.User().Role == "ADMIN";

    /// <summary>AI 客服/农事助手对话。</summary>
    [HttpPost("chat")]
    public async Task<ActionResult<ApiResponse<AiChatResponse>>> Chat([FromBody] AiChatRequest req, CancellationToken ct)
        => Ok(ApiResponse<AiChatResponse>.Ok(await _chat.ChatAsync(Uid, req, ct)));

    /// <summary>当前用户的 AI 会话列表。</summary>
    [HttpGet("conversations")]
    public async Task<ActionResult<ApiResponse<AiConversationPageDto>>> Conversations(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(ApiResponse<AiConversationPageDto>.Ok(await _chat.ListConversationsAsync(Uid, page, pageSize)));

    /// <summary>会话消息分页 (游标 beforeId)。</summary>
    [HttpGet("conversations/{id:long}/messages")]
    public async Task<ActionResult<ApiResponse<AiMessagePageDto>>> Messages(
        long id, [FromQuery] long? beforeId, [FromQuery] int limit = 20)
        => Ok(ApiResponse<AiMessagePageDto>.Ok(await _chat.ListMessagesAsync(Uid, id, beforeId, limit)));

    /// <summary>删除 AI 会话 (含消息)。</summary>
    [HttpDelete("conversations/{id:long}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteConversation(long id)
    {
        await _chat.DeleteConversationAsync(Uid, id);
        return Ok(ApiResponse<object>.Ok(null, "已删除"));
    }

    /// <summary>重命名 AI 会话。</summary>
    [HttpPut("conversations/{id:long}")]
    public async Task<ActionResult<ApiResponse<object>>> RenameConversation(long id, [FromBody] AiConversationRenameRequest req)
    {
        await _chat.RenameConversationAsync(Uid, id, req.Title);
        return Ok(ApiResponse<object>.Ok(null, "已保存"));
    }

    /// <summary>内容发布辅助 (生成描述/分类/价格区间)。</summary>
    [HttpPost("assist/publish")]
    public async Task<ActionResult<ApiResponse<AiAssistResponse>>> Assist([FromBody] AiAssistRequest req, CancellationToken ct)
        => Ok(ApiResponse<AiAssistResponse>.Ok(await _assist.AssistPublishAsync(Uid, req, ct)));

    /// <summary>大屏智能解读 (仅管理员)。</summary>
    [HttpPost("insight")]
    public async Task<ActionResult<ApiResponse<AiInsightResponse>>> Insight(CancellationToken ct)
    {
        RequireAdmin();
        return Ok(ApiResponse<AiInsightResponse>.Ok(await _insight.InsightAsync(Uid, ct)));
    }

    /// <summary>大屏数据问答 (仅管理员)。</summary>
    [HttpPost("ask")]
    public async Task<ActionResult<ApiResponse<AiAskResponse>>> Ask([FromBody] AiAskRequest req, CancellationToken ct)
    {
        RequireAdmin();
        return Ok(ApiResponse<AiAskResponse>.Ok(await _insight.AskAsync(Uid, req, ct)));
    }

    private void RequireAdmin()
    {
        if (!IsAdmin(HttpContext))
            throw new BizException("需要管理员权限", ErrorCodes.Forbidden);
    }
}
