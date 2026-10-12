using AgroMarket.Api.Common;
using AgroMarket.Api.Dtos;
using AgroMarket.Api.Chat.Application;
using AgroMarket.Api.Chat.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Chat.Api;

[ApiController]
[Route("api/chat")]
[RequireAuth]
public class ChatController : ControllerBase
{
    private readonly ChatService _chat;
    private readonly ChatTradeService _trade;
    public ChatController(ChatService chat, ChatTradeService trade) { _chat = chat; _trade = trade; }

    /// <summary>发起或复用与某用户的会话 (P2P)。</summary>
    [HttpPost("conversations")]
    public async Task<ActionResult<ApiResponse<ChatConversationDto>>> CreateOrGet(
        [FromBody] ConversationCreateRequest req)
        => Ok(ApiResponse<ChatConversationDto>.Ok(await _chat.CreateOrGetAsync(HttpContext.User().Id, req)));

    /// <summary>会话列表。filter=ALL|UNREAD; keyword 匹配对方昵称。</summary>
    [HttpGet("conversations")]
    public async Task<ActionResult<ApiResponse<PagedResult<ChatConversationDto>>>> List(
        [FromQuery] string? filter, [FromQuery] string? keyword,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(ApiResponse<PagedResult<ChatConversationDto>>.Ok(
            await _chat.ListAsync(HttpContext.User().Id, filter, keyword, page, pageSize)));

    /// <summary>会话详情。</summary>
    [HttpGet("conversations/{id:long}")]
    public async Task<ActionResult<ApiResponse<ChatConversationDto>>> Get(long id)
        => Ok(ApiResponse<ChatConversationDto>.Ok(await _chat.GetAsync(HttpContext.User().Id, id)));

    /// <summary>会话设置 (置顶/免打扰)。</summary>
    [HttpPut("conversations/{id:long}/settings")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateSettings(long id, [FromBody] ConversationSettingsRequest req)
    {
        await _chat.UpdateSettingsAsync(HttpContext.User().Id, id, req);
        return Ok(ApiResponse<object>.Ok(null, "已更新"));
    }

    /// <summary>删除(隐藏)会话, 仅对当前用户生效。</summary>
    [HttpDelete("conversations/{id:long}")]
    public async Task<ActionResult<ApiResponse<object>>> Hide(long id)
    {
        await _chat.HideAsync(HttpContext.User().Id, id);
        return Ok(ApiResponse<object>.Ok(null, "已删除"));
    }

    /// <summary>加载消息 (游标分页, beforeId 为空取最新)。</summary>
    [HttpGet("conversations/{id:long}/messages")]
    public async Task<ActionResult<ApiResponse<ChatMessagePageDto>>> Messages(
        long id, [FromQuery] long? beforeId, [FromQuery] int limit = 20)
        => Ok(ApiResponse<ChatMessagePageDto>.Ok(
            await _chat.ListMessagesAsync(HttpContext.User().Id, id, beforeId, limit)));

    /// <summary>发送消息 (clientMsgId 幂等)。</summary>
    [HttpPost("conversations/{id:long}/messages")]
    public async Task<ActionResult<ApiResponse<ChatMessageDto>>> Send(long id, [FromBody] MessageSendRequest req)
        => Ok(ApiResponse<ChatMessageDto>.Ok(await _chat.SendAsync(HttpContext.User().Id, id, req)));

    /// <summary>标记会话已读。</summary>
    [HttpPost("conversations/{id:long}/read")]
    public async Task<ActionResult<ApiResponse<object>>> MarkRead(long id, [FromBody] MarkReadRequest req)
    {
        await _chat.MarkReadAsync(HttpContext.User().Id, id, req.LastReadMessageId);
        return Ok(ApiResponse<object>.Ok(null, "已读"));
    }

    /// <summary>送达回执 (对方消息已到达本地)。</summary>
    [HttpPost("conversations/{id:long}/delivered")]
    public async Task<ActionResult<ApiResponse<object>>> Delivered(long id, [FromBody] DeliveredRequest req)
    {
        await _chat.MarkDeliveredAsync(HttpContext.User().Id, id, req.LastDeliveredMessageId);
        return Ok(ApiResponse<object>.Ok(null, "已送达"));
    }

    /// <summary>输入状态广播 (不落库)。</summary>
    [HttpPost("conversations/{id:long}/typing")]
    public async Task<ActionResult<ApiResponse<object>>> Typing(long id, [FromBody] TypingRequest req)
    {
        await _chat.TypingAsync(HttpContext.User().Id, id, req.Typing);
        return Ok(ApiResponse<object>.Ok(null, "ok"));
    }

    /// <summary>撤回消息 (2 分钟内, 仅发送者)。</summary>
    [HttpPost("messages/{messageId:long}/recall")]
    public async Task<ActionResult<ApiResponse<object>>> Recall(long messageId)
    {
        await _chat.RecallAsync(HttpContext.User().Id, messageId);
        return Ok(ApiResponse<object>.Ok(null, "已撤回"));
    }

    /// <summary>消息搜索。conversationId 为空时全局搜索, 指定时仅在该会话内搜索。</summary>
    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<List<ChatSearchResultDto>>>> Search(
        [FromQuery] string keyword, [FromQuery] long? conversationId, [FromQuery] int limit = 20)
        => Ok(ApiResponse<List<ChatSearchResultDto>>.Ok(
            await _chat.SearchAsync(HttpContext.User().Id, keyword, conversationId, limit)));

    /// <summary>未读消息总数。</summary>
    [HttpGet("unread")]
    public async Task<ActionResult<ApiResponse<long>>> Unread()
        => Ok(ApiResponse<long>.Ok(await _chat.UnreadTotalAsync(HttpContext.User().Id)));

    // ---------------- 会话内交易 ----------------

    /// <summary>分享供应商品/采购需求到会话 (PRODUCT_CARD)。</summary>
    [HttpPost("conversations/{id:long}/share-product")]
    public async Task<ActionResult<ApiResponse<ChatMessageDto>>> ShareProduct(long id, [FromBody] ShareProductRequest req)
        => Ok(ApiResponse<ChatMessageDto>.Ok(await _trade.ShareProductAsync(HttpContext.User().Id, id, req)));

    /// <summary>会话内发起报价 (挂 conversation_id, 推送 QUOTE 卡片)。</summary>
    [HttpPost("conversations/{id:long}/quote")]
    public async Task<ActionResult<ApiResponse<QuoteDto>>> Quote(long id, [FromBody] ChatQuoteRequest req)
        => Ok(ApiResponse<QuoteDto>.Ok(await _trade.CreateQuoteAsync(HttpContext.User().Id, id, req), "报价已发送"));

    /// <summary>从聊天直接下单 (生成已接受报价→订单, 推送 ORDER_CARD)。</summary>
    [HttpPost("conversations/{id:long}/order")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Order(long id, [FromBody] ChatOrderRequest req)
        => Ok(ApiResponse<OrderDto>.Ok(await _trade.CreateOrderAsync(HttpContext.User().Id, id, req), "订单已创建"));

    /// <summary>会话内接受报价。</summary>
    [HttpPost("conversations/{id:long}/quotes/{quoteId:long}/accept")]
    public async Task<ActionResult<ApiResponse<QuoteDto>>> AcceptQuote(long id, long quoteId)
        => Ok(ApiResponse<QuoteDto>.Ok(await _trade.AcceptQuoteAsync(HttpContext.User().Id, id, quoteId), "已接受报价"));

    /// <summary>会话内拒绝报价。</summary>
    [HttpPost("conversations/{id:long}/quotes/{quoteId:long}/reject")]
    public async Task<ActionResult<ApiResponse<QuoteDto>>> RejectQuote(long id, long quoteId)
        => Ok(ApiResponse<QuoteDto>.Ok(await _trade.RejectQuoteAsync(HttpContext.User().Id, id, quoteId), "已拒绝报价"));

    /// <summary>会话内还价 (生成新报价, 复用原会话)。</summary>
    [HttpPost("conversations/{id:long}/quotes/{quoteId:long}/counter")]
    public async Task<ActionResult<ApiResponse<QuoteDto>>> CounterQuote(long id, long quoteId, [FromBody] ChatQuoteCounterRequest req)
        => Ok(ApiResponse<QuoteDto>.Ok(await _trade.CounterQuoteAsync(HttpContext.User().Id, id, quoteId, req), "已还价"));

    // ---------------- 拉黑管理 ----------------

    /// <summary>拉黑列表。</summary>
    [HttpGet("blocks")]
    public async Task<ActionResult<ApiResponse<List<ChatPeerDto>>>> Blocks()
        => Ok(ApiResponse<List<ChatPeerDto>>.Ok(await _chat.ListBlocksAsync(HttpContext.User().Id)));

    /// <summary>拉黑用户。</summary>
    [HttpPost("blocks")]
    public async Task<ActionResult<ApiResponse<object>>> AddBlock([FromBody] BlockRequest req)
    {
        await _chat.AddBlockAsync(HttpContext.User().Id, req.UserId);
        return Ok(ApiResponse<object>.Ok(null, "已拉黑"));
    }

    /// <summary>取消拉黑。</summary>
    [HttpDelete("blocks/{userId:long}")]
    public async Task<ActionResult<ApiResponse<object>>> RemoveBlock(long userId)
    {
        await _chat.RemoveBlockAsync(HttpContext.User().Id, userId);
        return Ok(ApiResponse<object>.Ok(null, "已取消拉黑"));
    }
}
