using Dapper;
using System.Text.Json;
using AgroMarket.Api.Chat.Realtime;

namespace AgroMarket.Api.Notifications;

/// <summary>
/// 统一推送门面: 站内消息落库 → WS 在线扇出(type=notify) → 微信订阅消息(异步尽力)。
/// 任一下游失败不影响业务主流程 (全路径吞异常)。
/// </summary>
public class NotificationPushService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _db;
    private readonly ChatConnectionManager _connections;
    private readonly WeChatPushService _wechat;
    private readonly ILogger<NotificationPushService> _logger;

    private static readonly SemaphoreSlim WxGate = new(4, 4);

    public NotificationPushService(IDbConnectionFactory db, ChatConnectionManager connections, WeChatPushService wechat, ILogger<NotificationPushService> logger)
    { _db = db; _connections = connections; _wechat = wechat; _logger = logger; }

    /// <summary>推送单条消息给一个接收人。返回站内消息 ID (失败为 0)。wxTemplateKey 省略时按 msgType 自动映射订阅模板。</summary>
    public async Task<long> NotifyAsync(long receiverId, long? senderId, string msgType, string title, string? content, long? bizId = null, string? wxTemplateKey = null, string? wxPage = null, Dictionary<string, string>? wxData = null)
    {
        long messageId = 0;
        try
        {
            using var conn = _db.Create();
            messageId = await conn.ExecuteScalarAsync<long>(
                "INSERT INTO user_message (receiver_id, sender_id, msg_type, title, content, biz_id) VALUES (@r, @s, @t, @ti, @c, @b); SELECT LAST_INSERT_ID();",
                new { r = receiverId, s = senderId, t = msgType, ti = title, c = content, b = bizId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "notify insert failed receiver={Receiver}", receiverId);
        }
        PushWs(receiverId, messageId, msgType, title, bizId);
        wxTemplateKey ??= TemplateKeyOf(msgType);
        if (wxTemplateKey != null && messageId > 0)
        {
            var mid = messageId;
            _ = Task.Run(async () =>
            {
                await WxGate.WaitAsync();
                try { await _wechat.SendSubscribeAsync(receiverId, wxTemplateKey, wxPage ?? "/pages/message/index", wxData ?? new Dictionary<string, string>()); }
                catch { }
                finally { WxGate.Release(); }
            });
        }
        return messageId;
    }

    /// <summary>推送单条消息 (系统消息, sender 为空)。</summary>
    public Task<long> NotifyAsync(long receiverId, string msgType, string title, string? content, long? bizId = null, string? wxTemplateKey = null)
        => NotifyAsync(receiverId, null, msgType, title, content, bizId, wxTemplateKey);

    /// <summary>消息类型 → 订阅模板 key 映射; 未命中类型不发微信。</summary>
    private static string? TemplateKeyOf(string msgType) => msgType switch
    {
        "QUOTE" => "QUOTE",
        "ORDER" => "ORDER",
        "AUDIT" or "REPORT" => "AUDIT",
        _ => null,
    };

    /// <summary>推送站内消息给全部 ACTIVE 管理员。</summary>
    public async Task NotifyAdminsAsync(string msgType, string title, string? content, long? bizId = null, string? wxTemplateKey = null)
    {
        try
        {
            using var conn = _db.Create();
            var adminIds = (await conn.QueryAsync<long>(
                "SELECT id FROM sys_user WHERE role='ADMIN' AND status='ACTIVE'", new { })).ToList();
            foreach (var id in adminIds)
                await NotifyAsync(id, null, msgType, title, content, bizId, wxTemplateKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "notify admins failed");
        }
    }

    private void PushWs(long receiverId, long messageId, string msgType, string title, long? bizId)
    {
        try
        {
            var frame = JsonSerializer.Serialize(new
            {
                type = "notify",
                messageId,
                msgType,
                title,
                bizId,
                createdAt = DateTime.Now,
            }, JsonOpts);
            foreach (var conn in _connections.Get(receiverId))
                _ = conn.SendAsync(frame);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ws notify failed receiver={Receiver}", receiverId);
        }
    }
}
