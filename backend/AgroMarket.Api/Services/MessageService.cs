using AgroMarket.Api.Common;
using AgroMarket.Api.Notifications;
using Dapper;

namespace AgroMarket.Api.Services;

public class MessageService
{
    private readonly IDbConnectionFactory _db;
    private readonly NotificationPushService _push;
    public MessageService(IDbConnectionFactory db, NotificationPushService push) { _db = db; _push = push; }

    public async Task<long> SendAsync(long receiverId, long? senderId, string msgType, string title, string? content, long? bizId = null)
    {
        // 统一走推送门面: 落库 + WS 在线扇出 + 微信订阅消息(异步尽力), 失败不阻断主流程
        try { return await _push.NotifyAsync(receiverId, senderId, msgType, title, content, bizId); }
        catch { return 0; }
    }

    public async Task<Dtos.PagedResult<Dtos.MessageDto>> QueryAsync(long userId, string? box, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;
        var where = box switch
        {
            "UNREAD" => "receiver_id=@uid AND is_read=0",
            _ => "receiver_id=@uid",
        };
        using var conn = _db.Create();
        var total = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM user_message WHERE {where}", new { uid = userId });
        var items = (await conn.QueryAsync<Dtos.MessageDto>(
            $@"SELECT id, sender_id SenderId, msg_type MsgType, title, content Content, biz_id BizId, is_read IsRead, created_at CreatedAt
               FROM user_message WHERE {where} ORDER BY id DESC LIMIT @limit OFFSET @offset",
            new { uid = userId, limit = pageSize, offset = (page - 1) * pageSize })).ToList();
        return new Dtos.PagedResult<Dtos.MessageDto>(items, total, page, pageSize);
    }

    public async Task MarkReadAsync(long userId, long id)
    {
        using var conn = _db.Create();
        await conn.ExecuteAsync("UPDATE user_message SET is_read=1 WHERE id=@id AND receiver_id=@uid", new { id, uid = userId });
    }

    public async Task MarkAllReadAsync(long userId)
    {
        using var conn = _db.Create();
        await conn.ExecuteAsync("UPDATE user_message SET is_read=1 WHERE receiver_id=@uid", new { uid = userId });
    }

    public async Task<long> UnreadCountAsync(long userId)
    {
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM user_message WHERE receiver_id=@uid AND is_read=0", new { uid = userId });
    }
}
