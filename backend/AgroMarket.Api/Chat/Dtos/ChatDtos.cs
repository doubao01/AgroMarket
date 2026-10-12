using System.Text.Json;

namespace AgroMarket.Api.Chat.Dtos;

// ---------------- 请求 ----------------

/// <summary>发起/复用会话。peerUserId 为对方用户ID。</summary>
public record ConversationCreateRequest(long PeerUserId, string? SourceType, long? SourceId);

/// <summary>会话设置(置顶/免打扰)。</summary>
public record ConversationSettingsRequest(bool? IsPinned, bool? IsMuted);

/// <summary>发送消息。clientMsgId 用于幂等。</summary>
public record MessageSendRequest(
    string? ClientMsgId,
    string MessageType,
    string? Content,
    JsonElement? Payload,
    string? BizType,
    long? BizId,
    long? ReplyToMessageId = null);

/// <summary>标记已读。</summary>
public record MarkReadRequest(long LastReadMessageId);

/// <summary>送达回执。</summary>
public record DeliveredRequest(long LastDeliveredMessageId);

/// <summary>输入状态。</summary>
public record TypingRequest(bool Typing);

/// <summary>拉黑/取消拉黑目标。</summary>
public record BlockRequest(long UserId);

/// <summary>分享商品/需求到会话 (二选一)。</summary>
public record ShareProductRequest(long? SupplyId, long? DemandId);

/// <summary>会话内发起报价。价格/数量/单位须与发布信息一致。</summary>
public record ChatQuoteRequest(
    long? SupplyId, long? DemandId, decimal Price, string PriceUnit, decimal Quantity, string? Remark);

/// <summary>从聊天直接下单 (自动生成已接受报价)。</summary>
public record ChatOrderRequest(
    long? SupplyId, long? DemandId, decimal Price, string PriceUnit, decimal Quantity,
    string? DeliveryMode, string? AddressSnapshot, string? Remark);

/// <summary>会话内还价 (生成新报价, 复用原会话)。</summary>
public record ChatQuoteCounterRequest(decimal Price, decimal? Quantity, string? Remark);

// ---------------- 响应 ----------------

/// <summary>会话对端(对方用户)摘要。</summary>
public class ChatPeerDto
{
    public long Id { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public bool Verified { get; set; }
    public string? SubjectName { get; set; }
}

/// <summary>会话摘要(会话列表项)。</summary>
public class ChatConversationDto
{
    public long Id { get; set; }
    public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    public ChatPeerDto Peer { get; set; } = new();
    public string? LastContent { get; set; }
    public string? LastMessageType { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public bool IsPinned { get; set; }
    public bool IsMuted { get; set; }
}

/// <summary>单条消息。</summary>
public class ChatMessageDto
{
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public long SenderId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string? Content { get; set; }
    public JsonElement? Payload { get; set; }
    public string? BizType { get; set; }
    public long? BizId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? ReplyToMessageId { get; set; }
    public DateTime? RecalledAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>消息分页。Items 按时间升序; NextCursor 为加载更早消息的游标(最旧消息ID)。</summary>
public class ChatMessagePageDto
{
    public List<ChatMessageDto> Items { get; set; } = new();
    public long? NextCursor { get; set; }
    public bool HasMore { get; set; }
    /// <summary>对方已读游标(用于计算己方消息的已读状态)。</summary>
    public long? PeerLastReadMessageId { get; set; }
    /// <summary>对方已送达游标。</summary>
    public long? PeerLastDeliveredMessageId { get; set; }
}

/// <summary>全局搜索结果: 会话 + 命中的消息。</summary>
public class ChatSearchResultDto
{
    public ChatConversationDto Conversation { get; set; } = new();
    public ChatMessageDto Message { get; set; } = new();
}
