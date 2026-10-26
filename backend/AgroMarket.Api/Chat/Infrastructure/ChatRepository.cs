using System.Data;
using AgroMarket.Api.Data;
using AgroMarket.Api.Chat.Domain;
using AgroMarket.Api.Chat.Dtos;
using Dapper;

namespace AgroMarket.Api.Chat.Infrastructure;

// ---------------- Dapper 行映射类型 (属性类) ----------------

public class ChatMessageRow
{
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public long SenderId { get; set; }
    public string? ClientMsgId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? Payload { get; set; }
    public string? BizType { get; set; }
    public long? BizId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? ReplyToMessageId { get; set; }
    public DateTime? RecalledAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ChatConversationRow
{
    public long Id { get; set; }
    public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public bool IsPinned { get; set; }
    public bool IsMuted { get; set; }
    public string? Remark { get; set; }
    public long PeerId { get; set; }
    public string PeerNickname { get; set; } = string.Empty;
    public string? PeerAvatar { get; set; }
    public string? SubjectName { get; set; }
    public string? AuditStatus { get; set; }
    public string? LastContent { get; set; }
    public string? LastMessageType { get; set; }
}

/// <summary>全局搜索的扁平行: 会话摘要 + 匹配消息。</summary>
public class ChatSearchRow : ChatConversationRow
{
    public long? MatchedMessageId { get; set; }
    public long? MatchedSenderId { get; set; }
    public string? MatchedContent { get; set; }
    public DateTime? MatchedCreatedAt { get; set; }
    /// <summary>匹配消息的类型/状态, 缺失会让前端按类型分支渲染时出错。</summary>
    public string? MatchedMessageType { get; set; }
    public string? MatchedStatus { get; set; }
}

/// <summary>聊天数据访问 (Dapper)。读取方法自开连接; 复合写操作在仓储内使用事务。</summary>
public class ChatRepository
{
    private readonly IDbConnectionFactory _db;
    public ChatRepository(IDbConnectionFactory db) => _db = db;

    private const string MessageSelect = @"
SELECT id AS Id, conversation_id AS ConversationId, sender_id AS SenderId,
       client_msg_id AS ClientMsgId, message_type AS MessageType, content AS Content,
       payload AS Payload, biz_type AS BizType, biz_id AS BizId, status AS Status,
       reply_to_message_id AS ReplyToMessageId, recalled_at AS RecalledAt, created_at AS CreatedAt
FROM chat_message";

    private const string ConversationSelect = @"
SELECT c.id AS Id, c.source_type AS SourceType, c.source_id AS SourceId,
       c.last_message_at AS LastMessageAt,
       me.unread_count AS UnreadCount, me.is_pinned AS IsPinned, me.is_muted AS IsMuted,
       me.remark AS Remark,
       u.id AS PeerId, u.nickname AS PeerNickname, u.avatar_url AS PeerAvatar,
       mp.subject_name AS SubjectName, mp.audit_status AS AuditStatus,
       (SELECT CASE WHEN m.status='RECALLED' THEN '[已撤回]' ELSE m.content END
          FROM chat_message m WHERE m.id = c.last_message_id) AS LastContent,
       (SELECT m.message_type FROM chat_message m WHERE m.id = c.last_message_id) AS LastMessageType
FROM chat_participant me
JOIN chat_conversation c  ON c.id = me.conversation_id
JOIN chat_participant peer ON peer.conversation_id = c.id AND peer.user_id <> me.user_id
JOIN sys_user u           ON u.id = peer.user_id
LEFT JOIN merchant_profile mp ON mp.user_id = peer.user_id";

    /// <summary>
    /// 全局搜索查询串: 在用户参与且未隐藏的会话中, 命中关键词的消息里取最新一条。
    /// 用 JOIN 派生自查询(而非多个重复的相关子查询 + EXISTS)一次性取回匹配消息,
    /// 既减少重复扫描, 又保证四列来自同一条记录; 关键词仅匹配未撤回的文本/卡片类消息。
    /// </summary>
    private const string ConversationSearchSelect = @"
SELECT c.id AS Id, c.source_type AS SourceType, c.source_id AS SourceId,
       c.last_message_at AS LastMessageAt,
       me.unread_count AS UnreadCount, me.is_pinned AS IsPinned, me.is_muted AS IsMuted,
       me.remark AS Remark,
       u.id AS PeerId, u.nickname AS PeerNickname, u.avatar_url AS PeerAvatar,
       mp.subject_name AS SubjectName, mp.audit_status AS AuditStatus,
       (SELECT CASE WHEN m.status='RECALLED' THEN '[已撤回]' ELSE m.content END
          FROM chat_message m WHERE m.id = c.last_message_id) AS LastContent,
       (SELECT m.message_type FROM chat_message m WHERE m.id = c.last_message_id) AS LastMessageType,
       mm.id AS MatchedMessageId, mm.sender_id AS MatchedSenderId, mm.content AS MatchedContent,
       mm.created_at AS MatchedCreatedAt, mm.message_type AS MatchedMessageType, mm.status AS MatchedStatus
FROM chat_participant me
JOIN chat_conversation c  ON c.id = me.conversation_id
JOIN chat_participant peer ON peer.conversation_id = c.id AND peer.user_id <> me.user_id
JOIN sys_user u           ON u.id = peer.user_id
LEFT JOIN merchant_profile mp ON mp.user_id = peer.user_id
JOIN chat_message mm ON mm.id = (
    SELECT m.id FROM chat_message m
    WHERE m.conversation_id = c.id AND m.status <> 'RECALLED'
      AND m.message_type IN ('TEXT','PRODUCT_CARD','QUOTE','ORDER_CARD')
      AND m.content LIKE CONCAT('%',@kw,'%')
    ORDER BY m.id DESC LIMIT 1)
WHERE me.user_id=@uid AND me.is_hidden=0
ORDER BY c.last_message_at DESC, c.id DESC LIMIT @limit";

    // ---------------- 会话 ----------------

    public async Task<long?> FindConversationIdByKeyAsync(string convKey)
    {
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<long?>(
            "SELECT id FROM chat_conversation WHERE conv_key=@k", new { k = convKey });
    }

    public async Task<ChatConversationDto?> GetConversationForUserAsync(long convId, long userId)
    {
        using var conn = _db.Create();
        var row = await conn.QuerySingleOrDefaultAsync<ChatConversationRow>(
            ConversationSelect + " WHERE me.conversation_id=@c AND me.user_id=@uid",
            new { c = convId, uid = userId });
        return row is null ? null : MapConversation(row);
    }

    public async Task<List<ChatConversationDto>> ListConversationsAsync(
        long userId, string filter, string? keyword, int limit, int offset)
    {
        var where = "me.user_id=@uid AND me.is_hidden=0";
        if (filter == ChatFilter.Unread) where += " AND me.unread_count > 0";
        var hasKw = !string.IsNullOrWhiteSpace(keyword);
        if (hasKw) where += " AND u.nickname LIKE @kw";
        var sql = ConversationSelect + $" WHERE {where} " +
                  "ORDER BY me.is_pinned DESC, c.last_message_at DESC, c.id DESC LIMIT @limit OFFSET @offset";
        using var conn = _db.Create();
        var rows = await conn.QueryAsync<ChatConversationRow>(sql,
            new { uid = userId, kw = $"%{EscapeLike(keyword)}%", limit, offset });
        return rows.Select(MapConversation).ToList();
    }

    public async Task<long> CountConversationsAsync(long userId, string filter, string? keyword)
    {
        var where = "me.user_id=@uid AND me.is_hidden=0";
        if (filter == ChatFilter.Unread) where += " AND me.unread_count > 0";
        var hasKw = !string.IsNullOrWhiteSpace(keyword);
        if (hasKw) where += " AND u.nickname LIKE @kw";
        var sql = $@"SELECT COUNT(*) FROM chat_participant me
                     JOIN chat_conversation c ON c.id = me.conversation_id
                     JOIN chat_participant peer ON peer.conversation_id = c.id AND peer.user_id <> me.user_id
                     JOIN sys_user u ON u.id = peer.user_id
                     WHERE {where}";
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<long>(sql, new { uid = userId, kw = $"%{EscapeLike(keyword)}%" });
    }

    /// <summary>转义 LIKE 通配符: 用户输入 % 或 _ 时不应变成"匹配全部"。</summary>
    private static string EscapeLike(string? s)
        => (s ?? "").Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public async Task<bool> IsParticipantAsync(long convId, long userId)
    {
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM chat_participant WHERE conversation_id=@c AND user_id=@uid",
            new { c = convId, uid = userId }) > 0;
    }

    public async Task<long?> FindPeerIdAsync(long convId, long userId)
    {
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<long?>(
            "SELECT user_id FROM chat_participant WHERE conversation_id=@c AND user_id<>@uid LIMIT 1",
            new { c = convId, uid = userId });
    }

    public async Task<List<long>> GetParticipantIdsAsync(long convId)
    {
        using var conn = _db.Create();
        var ids = await conn.QueryAsync<long>(
            "SELECT user_id FROM chat_participant WHERE conversation_id=@c", new { c = convId });
        return ids.ToList();
    }

    public async Task<bool> UserExistsAsync(long userId)
    {
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys_user WHERE id=@id AND status='ACTIVE'", new { id = userId }) > 0;
    }

    public async Task<bool> IsBlockedEitherAsync(long a, long b)
    {
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM chat_block WHERE (blocker_id=@a AND blocked_user_id=@b) OR (blocker_id=@b AND blocked_user_id=@a)",
            new { a, b }) > 0;
    }

    /// <summary>拉黑 (幂等)。</summary>
    public async Task AddBlockAsync(long blockerId, long blockedId)
    {
        using var conn = _db.Create();
        await conn.ExecuteAsync(
            "INSERT INTO chat_block (blocker_id, blocked_user_id) VALUES (@b, @t) ON DUPLICATE KEY UPDATE created_at=created_at",
            new { b = blockerId, t = blockedId });
    }

    /// <summary>取消拉黑。返回受影响行数。</summary>
    public async Task<int> RemoveBlockAsync(long blockerId, long blockedId)
    {
        using var conn = _db.Create();
        return await conn.ExecuteAsync(
            "DELETE FROM chat_block WHERE blocker_id=@b AND blocked_user_id=@t",
            new { b = blockerId, t = blockedId });
    }

    /// <summary>当前用户拉黑的用户列表。</summary>
    public async Task<List<ChatPeerDto>> ListBlockedAsync(long userId)
    {
        using var conn = _db.Create();
        var rows = await conn.QueryAsync<ChatPeerDto>(
            @"SELECT u.id AS Id, u.nickname AS Nickname, u.avatar_url AS AvatarUrl
              FROM chat_block b JOIN sys_user u ON u.id = b.blocked_user_id
              WHERE b.blocker_id=@uid ORDER BY b.created_at DESC",
            new { uid = userId });
        return rows.ToList();
    }

    /// <summary>创建会话+双方成员 (事务)。并发重复时依赖 uk_conv_key, 冲突后重查返回既有会话。</summary>
    public async Task<long> CreateConversationAsync(string convKey, string? sourceType, long? sourceId, long userId, long peerId)
    {
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var convId = await conn.ExecuteScalarAsync<long>(
                @"INSERT INTO chat_conversation (conv_key, type, source_type, source_id)
                  VALUES (@k, 'P2P', @st, @sid); SELECT LAST_INSERT_ID();",
                new { k = convKey, st = sourceType, sid = sourceId }, tx);
            await conn.ExecuteAsync(
                "INSERT INTO chat_participant (conversation_id, user_id) VALUES (@c, @u), (@c, @p)",
                new { c = convId, u = userId, p = peerId }, tx);
            tx.Commit();
            return convId;
        }
        catch (MySqlConnector.MySqlException ex) when (ex.Number == 1062)
        {
            tx.Rollback();
            return await FindConversationIdByKeyAsync(convKey)
                ?? throw new Common.BizException("会话创建冲突, 请重试", Common.ErrorCodes.Conflict);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task UpdateSettingsAsync(long convId, long userId, bool? isPinned, bool? isMuted, string? remark = null)
    {
        using var conn = _db.Create();
        await conn.ExecuteAsync(
            @"UPDATE chat_participant SET
                is_pinned = COALESCE(@pin, is_pinned),
                is_muted  = COALESCE(@mute, is_muted),
                remark    = CASE WHEN @remark IS NULL THEN remark ELSE NULLIF(TRIM(@remark), '') END
              WHERE conversation_id=@c AND user_id=@uid",
            new { pin = isPinned, mute = isMuted, remark, c = convId, uid = userId });
    }

    public async Task HideAsync(long convId, long userId)
    {
        using var conn = _db.Create();
        // 隐藏必须先清零未读: 否则下次对方来消息重新显示会话时, 会把删除前的老未读叠加回来
        await conn.ExecuteAsync(
            "UPDATE chat_participant SET is_hidden=1, unread_count=0 WHERE conversation_id=@c AND user_id=@uid",
            new { c = convId, uid = userId });
    }

    // ---------------- 消息 ----------------

    public async Task<ChatMessageRow?> FindMessageByClientIdAsync(long convId, string clientMsgId)
    {
        using var conn = _db.Create();
        return await conn.QuerySingleOrDefaultAsync<ChatMessageRow>(
            MessageSelect + " WHERE conversation_id=@c AND client_msg_id=@cid",
            new { c = convId, cid = clientMsgId });
    }

    /// <summary>
    /// 发送消息 (事务): 落库 + 更新会话末消息 + 对方未读+1 + 双方取消隐藏。
    /// clientMsgId 命中唯一键(1062)时视为幂等重发: 回滚并返回既有消息, IsNew=false, 调用方据此跳过实时广播(避免重复未读+1)。
    /// </summary>
    public async Task<(ChatMessageRow Row, bool IsNew)> SendMessageAsync(
        long convId, long senderId, string? clientMsgId, string messageType,
        string? content, string? payload, string? bizType, long? bizId, long? replyToMessageId)
    {
        // 死锁重试: 本事务固定"先取会话行 X 锁再插消息", 已把同会话并发串行化;
        // 仍可能与其它业务事务交叉加锁, 收到 1213/1205 时退避重试, 不把瞬时死锁暴露成 500
        for (var attempt = 1; ; attempt++)
        {
            try { return await SendMessageOnceAsync(convId, senderId, clientMsgId, messageType, content, payload, bizType, bizId, replyToMessageId); }
            catch (MySqlConnector.MySqlException ex) when (ex.Number is 1213 or 1205 && attempt < 3)
            {
                await Task.Delay(20 * attempt);
            }
        }
    }

    private async Task<(ChatMessageRow Row, bool IsNew)> SendMessageOnceAsync(
        long convId, long senderId, string? clientMsgId, string messageType,
        string? content, string? payload, string? bizType, long? bizId, long? replyToMessageId)
    {
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            // 必须先拿会话行写锁再插消息: chat_message 的外键在插入时对本行加共享锁,
            // 随后 UPDATE chat_conversation 又要升级为排他锁, 并发发送时 30%~50% 触发死锁
            await conn.ExecuteAsync("SELECT id FROM chat_conversation WHERE id=@c FOR UPDATE", new { c = convId }, tx);

            var id = await conn.ExecuteScalarAsync<long>(
                @"INSERT INTO chat_message
                    (conversation_id, sender_id, client_msg_id, message_type, content, payload, biz_type, biz_id, reply_to_message_id)
                  VALUES (@c, @s, @cid, @mt, @ct, @pl, @bt, @bi, @rid);
                  SELECT LAST_INSERT_ID();",
                new { c = convId, s = senderId, cid = clientMsgId, mt = messageType, ct = content, pl = payload, bt = bizType, bi = bizId, rid = replyToMessageId }, tx);

            await conn.ExecuteAsync(
                "UPDATE chat_conversation SET last_message_id=@m, last_message_at=NOW(3) WHERE id=@c",
                new { m = id, c = convId }, tx);

            await conn.ExecuteAsync(
                "UPDATE chat_participant SET unread_count = unread_count + 1, is_hidden = 0 WHERE conversation_id=@c AND user_id<>@s",
                new { c = convId, s = senderId }, tx);

            await conn.ExecuteAsync(
                "UPDATE chat_participant SET is_hidden = 0 WHERE conversation_id=@c AND user_id=@s",
                new { c = convId, s = senderId }, tx);

            tx.Commit();

            var row = await GetMessageAsync(id)
                ?? throw new Common.BizException("消息写入失败", Common.ErrorCodes.ServerError);
            return (row, true);
        }
        catch (MySqlConnector.MySqlException ex) when (ex.Number == 1062)
        {
            tx.Rollback();
            var existing = await FindMessageByClientIdAsync(convId, clientMsgId!)
                ?? throw new Common.BizException("消息重复写入", Common.ErrorCodes.Conflict);
            return (existing, false);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<ChatMessageRow?> GetMessageAsync(long id)
    {
        using var conn = _db.Create();
        return await conn.QuerySingleOrDefaultAsync<ChatMessageRow>(
            MessageSelect + " WHERE id=@id", new { id });
    }

    /// <summary>按游标加载消息。beforeId 为空时取最新; 返回按时间降序(调用方反转)。</summary>
    public async Task<List<ChatMessageRow>> ListMessagesAsync(long convId, long? beforeId, int limit)
    {
        var sql = beforeId is null
            ? MessageSelect + " WHERE conversation_id=@c ORDER BY id DESC LIMIT @limit"
            : MessageSelect + " WHERE conversation_id=@c AND id < @before ORDER BY id DESC LIMIT @limit";
        using var conn = _db.Create();
        var rows = await conn.QueryAsync<ChatMessageRow>(sql, new { c = convId, before = beforeId, limit });
        return rows.ToList();
    }

    /// <summary>会话内按关键词搜索消息 (内容/商品名等文本, 排除撤回)。返回按时间降序。</summary>
    public async Task<List<ChatMessageRow>> SearchMessagesAsync(long convId, string keyword, int limit)
    {
        using var conn = _db.Create();
        var rows = await conn.QueryAsync<ChatMessageRow>(
            MessageSelect + @" WHERE conversation_id=@c AND status<>'RECALLED'
                 AND message_type IN ('TEXT','PRODUCT_CARD','QUOTE','ORDER_CARD')
                 AND content LIKE CONCAT('%',@kw,'%')
               ORDER BY id DESC LIMIT @limit",
            new { c = convId, kw = EscapeLike(keyword), limit });
        return rows.ToList();
    }

    /// <summary>全局搜索: 在用户参与的会话中按关键词搜索消息, 每个会话返回最新匹配。返回 (会话行, 消息行) 列表。</summary>
    public async Task<List<(ChatConversationRow Conv, ChatMessageRow Msg)>> SearchConversationsByMessageAsync(
        long userId, string keyword, int limit)
    {
        using var conn = _db.Create();
        var rows = await conn.QueryAsync<ChatSearchRow>(
            ConversationSearchSelect,
            new { uid = userId, kw = keyword, limit });

        var results = new List<(ChatConversationRow, ChatMessageRow)>();
        foreach (var r in rows)
        {
            // 匹配消息需带上 message_type/status, 否则前端按类型分支渲染(search 结果卡片)会拿到空类型。
            var msg = new ChatMessageRow
            {
                Id = r.MatchedMessageId ?? 0, ConversationId = r.Id,
                SenderId = r.MatchedSenderId ?? 0, Content = r.MatchedContent,
                MessageType = r.MatchedMessageType ?? string.Empty,
                Status = r.MatchedStatus ?? string.Empty,
                CreatedAt = r.MatchedCreatedAt ?? default,
            };
            results.Add((r, msg));
        }
        return results;
    }

    /// <summary>标记已读: 更新 last_read_message_id 并重算未读。返回受影响行数。</summary>
    public async Task<int> MarkReadAsync(long convId, long userId, long lastReadMessageId)
    {
        using var conn = _db.Create();
        // 客户端普遍用 long.MaxValue 表示"全部已读", 因此不能直接拒绝越界游标;
        // 统一夹到本会话最大消息ID, 既兼容该用法, 又避免伪造游标把已读推到不存在的位置
        if (lastReadMessageId > 0)
        {
            var maxId = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(id),0) FROM chat_message WHERE conversation_id=@c", new { c = convId });
            if (maxId > 0) lastReadMessageId = Math.Min(lastReadMessageId, maxId);
        }
        return await conn.ExecuteAsync(
            // 未读数必须按"生效游标"(GREATEST 结果)重算: 多端/乱序上报旧游标时,
            // 若用原始入参计算会把已读会话的未读重新放大(未读"复活")
            @"UPDATE chat_participant
              SET unread_count = (SELECT COUNT(*) FROM chat_message m
                                  WHERE m.conversation_id=@c
                                    AND m.id > GREATEST(COALESCE(last_read_message_id,0), @lr)
                                    AND m.sender_id <> @uid AND m.status <> 'RECALLED'),
                  last_read_message_id = GREATEST(COALESCE(last_read_message_id,0), @lr)
              WHERE conversation_id=@c AND user_id=@uid",
            new { c = convId, uid = userId, lr = lastReadMessageId });
    }

    public async Task<long> UnreadTotalAsync(long userId)
    {
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(unread_count),0) FROM chat_participant WHERE user_id=@uid AND is_hidden=0",
            new { uid = userId });
    }

    /// <summary>对方(非当前用户)的已读/已送达游标, 用于计算己方消息状态。</summary>
    public async Task<(long? LastRead, long? LastDelivered)> GetPeerCursorsAsync(long convId, long userId)
    {
        using var conn = _db.Create();
        var row = await conn.QuerySingleOrDefaultAsync<(long? LastRead, long? LastDelivered)>(
            @"SELECT last_read_message_id AS LastRead, last_delivered_message_id AS LastDelivered
              FROM chat_participant WHERE conversation_id=@c AND user_id<>@uid LIMIT 1",
            new { c = convId, uid = userId });
        return row;
    }

    /// <summary>更新送达游标(只增不减)。返回受影响行数。</summary>
    public async Task<int> MarkDeliveredAsync(long convId, long userId, long lastDeliveredMessageId)
    {
        using var conn = _db.Create();
        return await conn.ExecuteAsync(
            @"UPDATE chat_participant
              SET last_delivered_message_id = GREATEST(COALESCE(last_delivered_message_id,0), @ld)
              WHERE conversation_id=@c AND user_id=@uid",
            new { c = convId, uid = userId, ld = lastDeliveredMessageId });
    }

    /// <summary>消息元信息(用于撤回校验)。</summary>
    public async Task<(long ConversationId, long SenderId, DateTime CreatedAt, string Status)?> GetMessageMetaAsync(long messageId)
    {
        using var conn = _db.Create();
        var row = await conn.QuerySingleOrDefaultAsync<(long ConversationId, long SenderId, DateTime CreatedAt, string Status)?>(
            "SELECT conversation_id AS ConversationId, sender_id AS SenderId, created_at AS CreatedAt, status AS Status FROM chat_message WHERE id=@id",
            new { id = messageId });
        return row;
    }

    /// <summary>消息是否属于指定会话。</summary>
    public async Task<bool> MessageBelongsToConversationAsync(long messageId, long convId)
    {
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM chat_message WHERE id=@id AND conversation_id=@c",
            new { id = messageId, c = convId }) > 0;
    }

    /// <summary>撤回消息: 仅发送者、2 分钟内、未撤回。返回受影响行数。</summary>
    public async Task<int> RecallAsync(long messageId, long senderId, int windowMinutes)
    {
        using var conn = _db.Create();
        var affected = await conn.ExecuteAsync(
            @"UPDATE chat_message SET status='RECALLED', recalled_at=NOW(3)
              WHERE id=@id AND sender_id=@s AND status='SENT'
                AND created_at > DATE_SUB(NOW(3), INTERVAL @w MINUTE)",
            new { id = messageId, s = senderId, w = windowMinutes });

        // 撤回必须同步回退未读数: unread_count 是增量维护的裸计数(发送时 +1),
        // 只改消息状态会让接收端红点比真实未读多, 且用户不进会话就永远不会修正。
        // 这里按"未读且未撤回"重算一次, 与 MarkReadAsync 用的是同一套口径。
        if (affected > 0)
        {
            await conn.ExecuteAsync(
                @"UPDATE chat_participant p
                  JOIN chat_message m0 ON m0.id = @id
                  SET p.unread_count = (SELECT COUNT(*) FROM chat_message m
                                        WHERE m.conversation_id = p.conversation_id
                                          AND m.id > COALESCE(p.last_read_message_id, 0)
                                          AND m.sender_id <> p.user_id
                                          AND m.status <> 'RECALLED')
                  WHERE p.conversation_id = m0.conversation_id AND p.user_id <> @s",
                new { id = messageId, s = senderId });
        }

        return affected;
    }

    // ---------------- 映射 ----------------

    /// <summary>会话行 -> DTO (供列表与搜索复用)。</summary>
    public static ChatConversationDto MapConversation(ChatConversationRow r) => new()
    {
        Id = r.Id,
        SourceType = r.SourceType,
        SourceId = r.SourceId,
        LastContent = r.LastContent,
        LastMessageType = r.LastMessageType,
        LastMessageAt = r.LastMessageAt,
        UnreadCount = r.UnreadCount,
        IsPinned = r.IsPinned,
        IsMuted = r.IsMuted,
        Remark = r.Remark,
        Peer = new ChatPeerDto
        {
            Id = r.PeerId,
            Nickname = r.PeerNickname,
            AvatarUrl = r.PeerAvatar,
            Verified = r.AuditStatus == "APPROVED",
            SubjectName = r.SubjectName,
        },
    };
}
