using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AgroMarket.Api.Chat.Application;
using AgroMarket.Api.Chat.Dtos;
using AgroMarket.Api.Common;
using Dapper;
using Microsoft.IdentityModel.Tokens;

namespace AgroMarket.Api.Chat.Realtime;

/// <summary>
/// 原生 WebSocket 聊天通道 (ADR-1)。协议为 JSON 文本帧:
/// 客户端 -> 服务端: {type:"ping"} | {type:"send",conversationId,clientMsgId,messageType,content,payload?,bizType?,bizId?,replyToMessageId?} | {type:"read",conversationId,lastReadMessageId} | {type:"delivered",conversationId,lastDeliveredMessageId} | {type:"typing",conversationId,typing} | {type:"recall",messageId}
/// 服务端 -> 客户端: {type:"connected"} | {type:"pong"} | {type:"message",...} | {type:"read",...} | {type:"delivered",...} | {type:"typing",...} | {type:"recall",...} | {type:"ack",...} | {type:"error",...}
/// 鉴权: 握手时从 query ?token= 或 Authorization/X-Token 头解析 JWT, 并校验账号为 ACTIVE。
/// </summary>
public static class ChatRealtimeHub
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task HandleAsync(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var userId = ResolveUserId(ctx, cfg);
        if (userId is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // 长连接不经过 HTTP 鉴权过滤器, 需在此额外校验账号状态:
        // 冻结/禁用用户即使持有未过期 JWT 也不得建立聊天长连接 (与 AuthHandler 的 ACTIVE 校验保持一致)。
        var db = ctx.RequestServices.GetRequiredService<IDbConnectionFactory>();
        using (var checkConn = db.Create())
        {
            var status = await checkConn.QuerySingleOrDefaultAsync<string>(
                "SELECT status FROM sys_user WHERE id=@id", new { id = userId.Value });
            if (status != "ACTIVE")
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        var mgr = ctx.RequestServices.GetRequiredService<ChatConnectionManager>();
        var chat = ctx.RequestServices.GetRequiredService<ChatService>();
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ChatRealtime");

        using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
        var conn = mgr.Add(userId.Value, socket);
        logger.LogInformation("chat ws connected user={UserId}", userId);

        try
        {
            await conn.SendAsync(JsonSerializer.Serialize(new { type = "connected", userId = userId.Value }, Json));

            var buffer = new byte[8 * 1024];
            while (socket.State == WebSocketState.Open && !ctx.RequestAborted.IsCancellationRequested)
            {
                var text = await ReceiveTextAsync(socket, buffer, ctx.RequestAborted);
                if (text is null) break;
                await DispatchAsync(conn, chat, logger, text);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            mgr.Remove(conn);
            if (socket.State == WebSocketState.Open)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch { /* ignore */ }
            }
            logger.LogInformation("chat ws disconnected user={UserId}", userId);
        }
    }

    private static async Task DispatchAsync(ChatConnection conn, ChatService chat, ILogger logger, string text)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(text);
            root = doc.RootElement.Clone();
        }
        catch (JsonException) { return; }

        var type = GetString(root, "type");
        switch (type)
        {
            case "ping":
                await conn.SendAsync(JsonSerializer.Serialize(new { type = "pong" }, Json));
                break;

            case "send":
                await HandleSendAsync(conn, chat, logger, root);
                break;

            case "read":
                await HandleReadAsync(conn, chat, root);
                break;

            case "delivered":
                await HandleDeliveredAsync(conn, chat, root);
                break;

            case "typing":
                await HandleTypingAsync(conn, chat, root);
                break;

            case "recall":
                await HandleRecallAsync(conn, chat, root);
                break;
        }
    }

    private static async Task HandleSendAsync(ChatConnection conn, ChatService chat, ILogger logger, JsonElement root)
    {
        if (!TryGetLong(root, "conversationId", out var convId))
        {
            await conn.SendAsync(JsonSerializer.Serialize(new { type = "error", code = 400, msg = "缺少 conversationId" }, Json));
            return;
        }

        var clientMsgId = GetString(root, "clientMsgId");
        var req = new MessageSendRequest(
            clientMsgId,
            GetString(root, "messageType") ?? ChatMessageTypeText,
            GetString(root, "content"),
            root.TryGetProperty("payload", out var pl) && pl.ValueKind != JsonValueKind.Null ? pl.Clone() : null,
            GetString(root, "bizType"),
            TryGetLong(root, "bizId", out var bid) ? bid : null,
            TryGetLong(root, "replyToMessageId", out var rid) ? rid : null);

        try
        {
            var msg = await chat.SendAsync(conn.UserId, convId, req);
            await conn.SendAsync(JsonSerializer.Serialize(
                new { type = "ack", clientMsgId, messageId = msg.Id, conversationId = convId }, Json));
        }
        catch (BizException ex)
        {
            await conn.SendAsync(JsonSerializer.Serialize(
                new { type = "error", code = ex.Code, msg = ex.Message, clientMsgId }, Json));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "chat ws send failed user={UserId} conv={ConvId}", conn.UserId, convId);
            await conn.SendAsync(JsonSerializer.Serialize(new { type = "error", code = 500, msg = "发送失败", clientMsgId }, Json));
        }
    }

    private static async Task HandleReadAsync(ChatConnection conn, ChatService chat, JsonElement root)
    {
        if (!TryGetLong(root, "conversationId", out var convId)) return;
        TryGetLong(root, "lastReadMessageId", out var lastRead);
        try { await chat.MarkReadAsync(conn.UserId, convId, lastRead); }
        catch (BizException) { /* 忽略越权/不存在 */ }
    }

    private static async Task HandleDeliveredAsync(ChatConnection conn, ChatService chat, JsonElement root)
    {
        if (!TryGetLong(root, "conversationId", out var convId)) return;
        TryGetLong(root, "lastDeliveredMessageId", out var lastDelivered);
        try { await chat.MarkDeliveredAsync(conn.UserId, convId, lastDelivered); }
        catch (BizException) { /* 忽略越权/不存在 */ }
    }

    private static async Task HandleTypingAsync(ChatConnection conn, ChatService chat, JsonElement root)
    {
        if (!TryGetLong(root, "conversationId", out var convId)) return;
        var typing = !root.TryGetProperty("typing", out var t) || t.ValueKind != JsonValueKind.False;
        try { await chat.TypingAsync(conn.UserId, convId, typing); }
        catch (BizException) { /* 忽略越权/不存在 */ }
    }

    private static async Task HandleRecallAsync(ChatConnection conn, ChatService chat, JsonElement root)
    {
        if (!TryGetLong(root, "messageId", out var messageId)) return;
        try { await chat.RecallAsync(conn.UserId, messageId); }
        catch (BizException ex)
        {
            await conn.SendAsync(JsonSerializer.Serialize(
                new { type = "error", code = ex.Code, msg = ex.Message, messageId }, Json));
        }
    }

    private const string ChatMessageTypeText = "TEXT";

    private static long? ResolveUserId(HttpContext ctx, IConfiguration cfg)
    {
        var token = ctx.Request.Query["token"].FirstOrDefault();
        if (string.IsNullOrEmpty(token))
        {
            var auth = ctx.Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = auth["Bearer ".Length..].Trim();
        }
        if (string.IsNullOrEmpty(token)) token = ctx.Request.Headers["X-Token"].ToString();
        if (string.IsNullOrEmpty(token)) return null;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(cfg["Jwt:SecretKey"]!);
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = "AgroMarket",
                ValidateAudience = true,
                ValidAudience = "AgroMarketWeb",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            }, out _);
            var id = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return id != null && long.TryParse(id, out var uid) ? uid : null;
        }
        catch { return null; }
    }

    /// <summary>读取一条完整文本消息 (处理分片)。连接关闭返回 null。</summary>
    private static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string? GetString(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static bool TryGetLong(JsonElement e, string name, out long value)
    {
        value = 0;
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.TryGetInt64(out value);
        return false;
    }
}
