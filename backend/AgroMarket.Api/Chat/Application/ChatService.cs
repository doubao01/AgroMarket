using System.Text.Json;
using AgroMarket.Api.Common;
using AgroMarket.Api.Dtos;
using AgroMarket.Api.Chat.Domain;
using AgroMarket.Api.Chat.Dtos;
using AgroMarket.Api.Chat.Infrastructure;
using AgroMarket.Api.Chat.Realtime;

namespace AgroMarket.Api.Chat.Application;

/// <summary>
/// 聊天用例编排。所有写操作先校验成员/拉黑, 交易状态变更不在此处发生 (ADR-2)。
/// </summary>
public class ChatService
{
    private readonly ChatRepository _repo;
    private readonly IRealtimeNotifier _realtime;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ChatService(ChatRepository repo, IRealtimeNotifier realtime)
    {
        _repo = repo;
        _realtime = realtime;
    }

    public async Task<ChatConversationDto> CreateOrGetAsync(long userId, ConversationCreateRequest req)
    {
        if (req.PeerUserId == userId)
            throw new BizException("不能与自己发起会话");
        if (!ChatSourceType.IsValid(req.SourceType))
            throw new BizException("来源类型不合法");
        if (!await _repo.UserExistsAsync(req.PeerUserId))
            throw new BizException("对方用户不存在或不可用", ErrorCodes.NotFound);
        if (await _repo.IsBlockedEitherAsync(userId, req.PeerUserId))
            throw new BizException("无法发起会话", ErrorCodes.Forbidden);

        var convKey = BuildConvKey(userId, req.PeerUserId, req.SourceType, req.SourceId);
        var convId = await _repo.FindConversationIdByKeyAsync(convKey)
                     ?? await _repo.CreateConversationAsync(convKey, req.SourceType, req.SourceId, userId, req.PeerUserId);

        return await _repo.GetConversationForUserAsync(convId, userId)
               ?? throw new BizException("会话不存在", ErrorCodes.ServerError);
    }

    public async Task<PagedResult<ChatConversationDto>> ListAsync(
        long userId, string? filter, string? keyword, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;
        var f = string.IsNullOrWhiteSpace(filter) ? ChatFilter.All : filter.ToUpperInvariant();
        if (f is not (ChatFilter.All or ChatFilter.Unread)) f = ChatFilter.All;

        var total = await _repo.CountConversationsAsync(userId, f, keyword);
        var items = await _repo.ListConversationsAsync(userId, f, keyword, pageSize, (page - 1) * pageSize);
        return new PagedResult<ChatConversationDto>(items, total, page, pageSize);
    }

    public async Task<ChatConversationDto> GetAsync(long userId, long convId)
    {
        await EnsureParticipantAsync(convId, userId);
        return await _repo.GetConversationForUserAsync(convId, userId)
               ?? throw new BizException("会话不存在", ErrorCodes.NotFound);
    }

    public async Task<ChatMessageDto> SendAsync(long userId, long convId, MessageSendRequest req)
    {
        await EnsureParticipantAsync(convId, userId);

        if (!ChatMessageType.IsValid(req.MessageType) ||
            !ChatMessageType.ClientSendable.Contains(req.MessageType))
            throw new BizException("不支持的消息类型");

        var content = req.Content?.Trim();
        if (req.MessageType == ChatMessageType.Text && string.IsNullOrEmpty(content))
            throw new BizException("消息内容不能为空");
        if (content is { Length: > 2000 })
            throw new BizException("消息内容过长");
        if (req.MessageType == ChatMessageType.Image)
        {
            var url = req.Payload is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty("url", out var u)
                ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(url)) throw new BizException("图片消息缺少 url");
        }
        if (req.ReplyToMessageId is > 0 &&
            !await _repo.MessageBelongsToConversationAsync(req.ReplyToMessageId.Value, convId))
            throw new BizException("引用的消息不存在");

        var peerId = await _repo.FindPeerIdAsync(convId, userId);
        if (peerId is null)
            throw new BizException("会话对方不存在", ErrorCodes.NotFound);
        if (await _repo.IsBlockedEitherAsync(userId, peerId.Value))
            throw new BizException("消息发送失败", ErrorCodes.Forbidden);

        var payload = req.Payload is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined }
            ? req.Payload.Value.GetRawText()
            : null;

        var row = await _repo.SendMessageAsync(
            convId, userId, req.ClientMsgId, req.MessageType, content, payload, req.BizType, req.BizId, req.ReplyToMessageId);

        var dto = MapMessage(row);
        await _realtime.NotifyMessageAsync(convId, dto, userId);
        return dto;
    }

    public async Task<ChatMessagePageDto> ListMessagesAsync(long userId, long convId, long? beforeId, int limit)
    {
        await EnsureParticipantAsync(convId, userId);
        if (limit is < 1 or > 50) limit = 20;

        var rows = await _repo.ListMessagesAsync(convId, beforeId, limit + 1);
        var hasMore = rows.Count > limit;
        if (hasMore) rows = rows.Take(limit).ToList();

        var items = rows.Select(MapMessage).ToList();
        items.Reverse();
        var (peerRead, peerDelivered) = await _repo.GetPeerCursorsAsync(convId, userId);
        return new ChatMessagePageDto
        {
            Items = items,
            HasMore = hasMore,
            NextCursor = hasMore && items.Count > 0 ? items[0].Id : null,
            PeerLastReadMessageId = peerRead,
            PeerLastDeliveredMessageId = peerDelivered,
        };
    }

    /// <summary>
    /// 消息搜索。conversationId 为空时全局搜索(在用户参与的会话中按关键词匹配, 返回命中会话+消息);
    /// 指定 conversationId 时仅在该会话内搜索(先校验成员)。
    /// </summary>
    public async Task<List<ChatSearchResultDto>> SearchAsync(long userId, string keyword, long? conversationId, int limit)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            throw new BizException("请输入搜索关键词");
        keyword = keyword.Trim();
        if (keyword.Length > 50) keyword = keyword[..50];
        if (limit is < 1 or > 50) limit = 20;

        if (conversationId is > 0)
        {
            await EnsureParticipantAsync(conversationId.Value, userId);
            var rows = await _repo.SearchMessagesAsync(conversationId.Value, keyword, limit);
            var conv = await _repo.GetConversationForUserAsync(conversationId.Value, userId);
            return rows.Select(r => new ChatSearchResultDto
            {
                Conversation = conv ?? new ChatConversationDto { Id = conversationId.Value },
                Message = MapMessage(r),
            }).ToList();
        }

        var hits = await _repo.SearchConversationsByMessageAsync(userId, keyword, limit);
        return hits.Select(h => new ChatSearchResultDto
        {
            Conversation = ChatRepository.MapConversation(h.Conv),
            Message = MapMessage(h.Msg),
        }).ToList();
    }

    public async Task MarkReadAsync(long userId, long convId, long lastReadMessageId)
    {
        await EnsureParticipantAsync(convId, userId);
        if (lastReadMessageId <= 0) return;
        await _repo.MarkReadAsync(convId, userId, lastReadMessageId);
        await _repo.MarkDeliveredAsync(convId, userId, lastReadMessageId);
        await _realtime.NotifyReadAsync(convId, userId, lastReadMessageId);
    }

    /// <summary>送达回执: 对方消息已到达本地(不改变已读)。</summary>
    public async Task MarkDeliveredAsync(long userId, long convId, long lastDeliveredMessageId)
    {
        await EnsureParticipantAsync(convId, userId);
        if (lastDeliveredMessageId <= 0) return;
        await _repo.MarkDeliveredAsync(convId, userId, lastDeliveredMessageId);
        await _realtime.NotifyDeliveredAsync(convId, userId, lastDeliveredMessageId);
    }

    /// <summary>撤回消息: 仅发送者、2 分钟内。返回被撤回消息所属会话。</summary>
    public async Task<long> RecallAsync(long userId, long messageId)
    {
        var meta = await _repo.GetMessageMetaAsync(messageId)
            ?? throw new BizException("消息不存在", ErrorCodes.NotFound);
        await EnsureParticipantAsync(meta.ConversationId, userId);
        if (meta.SenderId != userId)
            throw new BizException("只能撤回自己发送的消息", ErrorCodes.Forbidden);
        if (meta.Status == "RECALLED")
            throw new BizException("消息已撤回", ErrorCodes.Conflict);
        if (meta.CreatedAt < DateTime.Now.AddMinutes(-ChatMessageType.RecallWindowMinutes))
            throw new BizException($"超过 {ChatMessageType.RecallWindowMinutes} 分钟的消息不能撤回");

        var affected = await _repo.RecallAsync(messageId, userId, ChatMessageType.RecallWindowMinutes);
        if (affected == 0)
            throw new BizException("消息已超过撤回时限", ErrorCodes.Conflict);

        await _realtime.NotifyRecallAsync(meta.ConversationId, messageId, userId);
        return meta.ConversationId;
    }

    /// <summary>输入状态广播(不落库)。</summary>
    public async Task TypingAsync(long userId, long convId, bool typing)
    {
        await EnsureParticipantAsync(convId, userId);
        await _realtime.NotifyTypingAsync(convId, userId, typing);
    }

    public Task<long> UnreadTotalAsync(long userId) => _repo.UnreadTotalAsync(userId);

    // ---------------- 拉黑管理 ----------------

    /// <summary>当前用户拉黑的用户列表。</summary>
    public Task<List<ChatPeerDto>> ListBlocksAsync(long userId) => _repo.ListBlockedAsync(userId);

    public async Task AddBlockAsync(long userId, long targetId)
    {
        if (targetId == userId) throw new BizException("不能拉黑自己");
        if (!await _repo.UserExistsAsync(targetId))
            throw new BizException("对方用户不存在或不可用", ErrorCodes.NotFound);
        await _repo.AddBlockAsync(userId, targetId);
    }

    public async Task RemoveBlockAsync(long userId, long targetId)
    {
        var affected = await _repo.RemoveBlockAsync(userId, targetId);
        if (affected == 0) throw new BizException("未拉黑该用户", ErrorCodes.NotFound);
    }

    /// <summary>发送业务消息 (PRODUCT_CARD/QUOTE/ORDER_CARD 等), 由业务事件触发, 不受 ClientSendable 限制。</summary>
    public async Task<ChatMessageDto> SendBusinessAsync(
        long senderId, long convId, string messageType, string? content, object? payload, string? bizType = null, long? bizId = null)
    {
        if (!ChatMessageType.IsValid(messageType))
            throw new BizException("不支持的消息类型");
        var json = payload is null ? null : JsonSerializer.Serialize(payload, JsonOpts);
        var row = await _repo.SendMessageAsync(
            convId, senderId, null, messageType, content, json, bizType, bizId, null);
        var dto = MapMessage(row);
        await _realtime.NotifyMessageAsync(convId, dto, senderId);
        return dto;
    }

    /// <summary>确保当前用户是该会话成员, 且与对方未互相拉黑 (用于会话内业务操作)。返回对方用户ID。</summary>
    public async Task<long> EnsurePeerReachableAsync(long userId, long convId)
    {
        await EnsureParticipantAsync(convId, userId);
        var peerId = await _repo.FindPeerIdAsync(convId, userId)
            ?? throw new BizException("会话对方不存在", ErrorCodes.NotFound);
        if (await _repo.IsBlockedEitherAsync(userId, peerId))
            throw new BizException("对方不可联系", ErrorCodes.Forbidden);
        return peerId;
    }

    public async Task<ChatConversationDto> GetConversationAsync(long userId, long convId)
    {
        await EnsureParticipantAsync(convId, userId);
        return await _repo.GetConversationForUserAsync(convId, userId)
               ?? throw new BizException("会话不存在", ErrorCodes.NotFound);
    }

    public async Task UpdateSettingsAsync(long userId, long convId, ConversationSettingsRequest req)
    {
        await EnsureParticipantAsync(convId, userId);
        var remark = req.Remark?.Trim();
        if (remark is { Length: > 64 })
            throw new BizException("备注名过长(最多64字)", ErrorCodes.BadRequest);
        await _repo.UpdateSettingsAsync(convId, userId, req.IsPinned, req.IsMuted, req.Remark);
    }

    public async Task HideAsync(long userId, long convId)
    {
        await EnsureParticipantAsync(convId, userId);
        await _repo.HideAsync(convId, userId);
    }

    // ---------------- 内部 ----------------

    private async Task EnsureParticipantAsync(long convId, long userId)
    {
        if (!await _repo.IsParticipantAsync(convId, userId))
            throw new BizException("无权访问该会话", ErrorCodes.Forbidden);
    }

    private static string BuildConvKey(long a, long b, string? sourceType, long? sourceId)
    {
        var lo = Math.Min(a, b);
        var hi = Math.Max(a, b);
        return $"{lo}:{hi}:{sourceType ?? "-"}:{sourceId?.ToString() ?? "-"}";
    }

    private static ChatMessageDto MapMessage(ChatMessageRow r)
    {
        var recalled = r.Status == "RECALLED" || r.RecalledAt is not null;
        return new ChatMessageDto
        {
            Id = r.Id,
            ConversationId = r.ConversationId,
            SenderId = r.SenderId,
            MessageType = r.MessageType,
            Content = recalled ? null : r.Content,
            Payload = recalled || string.IsNullOrEmpty(r.Payload)
                ? null
                : JsonSerializer.Deserialize<JsonElement>(r.Payload, JsonOpts),
            BizType = r.BizType,
            BizId = r.BizId,
            Status = recalled ? "RECALLED" : r.Status,
            ReplyToMessageId = r.ReplyToMessageId,
            RecalledAt = r.RecalledAt,
            CreatedAt = r.CreatedAt,
        };
    }
}
