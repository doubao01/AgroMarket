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
        var auth = ResolveUserId(ctx, cfg);
        if (auth is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        var userId = auth.Value.UserId;
        var tokenExpiresAt = auth.Value.ExpiresAtUtc;

        // 长连接不经过 HTTP 鉴权过滤器, 需在此额外校验账号状态:
        // 冻结/禁用用户即使持有未过期 JWT 也不得建立聊天长连接 (与 AuthHandler 的 ACTIVE 校验保持一致)。
        var db = ctx.RequestServices.GetRequiredService<IDbConnectionFactory>();
        using (var checkConn = db.Create())
        {
            var status = await checkConn.QuerySingleOrDefaultAsync<string>(
                "SELECT status FROM sys_user WHERE id=@id", new { id = userId });
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
        var conn = mgr.Add(userId, socket);
        logger.LogInformation("chat ws connected user={UserId}", userId);

        try
        {
            await conn.SendAsync(JsonSerializer.Serialize(new { type = "connected", userId }, Json));

            var buffer = new byte[8 * 1024];
            // 最长等 60 秒就回循环顶部复核一次凭据与账号状态。
            // 原实现把校验放在收到帧之后: 从不发帧的"静默客户端"(挂着页面不发心跳、
            // 或干脆是脚本长期挂着)永远不进校验分支, 管理员冻结账号或 token 过期后
            // 这条连接仍会无限期收到消息推送 —— 封号不生效。
            var nextStatusCheck = DateTime.UtcNow.Add(RecheckInterval);
            while (socket.State == WebSocketState.Open && !ctx.RequestAborted.IsCancellationRequested)
            {
                if (DateTime.UtcNow >= tokenExpiresAt)
                {
                    await conn.SendAsync(JsonSerializer.Serialize(
                        new { type = "error", code = 401, msg = "登录已过期, 请重新登录" }, Json));
                    await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "token expired", CancellationToken.None);
                    break;
                }
                if (DateTime.UtcNow >= nextStatusCheck)
                {
                    nextStatusCheck = DateTime.UtcNow.Add(RecheckInterval);
                    if (!await IsUserActiveAsync(db, userId))
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "account inactive", CancellationToken.None);
                        break;
                    }
                }

                var (outcome, text) = await ReceiveTextOrIdleAsync(socket, buffer, ctx.RequestAborted, RecheckInterval);
                if (outcome == ReceiveOutcome.Idle) continue;   // 这段时间没收到帧, 回顶部复核状态
                if (outcome == ReceiveOutcome.TooLarge)
                {
                    await conn.SendAsync(JsonSerializer.Serialize(
                        new { type = "error", code = 413, msg = "消息过长, 单帧上限 64KB" }, Json));
                    await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "frame too large", CancellationToken.None);
                    break;
                }
                if (outcome == ReceiveOutcome.Closed) break;

                await DispatchAsync(conn, chat, logger, text!);   // Ok 分支的 Text 一定非空
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
        catch (JsonException)
        {
            // 畸形帧不能静默忽略: 客户端会以为消息进了链路, 服务端日志里也查不到是谁发的
            await conn.SendAsync(JsonSerializer.Serialize(
                new { type = "error", code = 400, msg = "帧格式错误, 需要 JSON 对象" }, Json));
            return;
        }

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

    /// <summary>上报类帧(read/delivered/typing)的统一错误回执, 带 op 便于客户端区分。</summary>
    private static Task SendOpErrorAsync(ChatConnection conn, string op, BizException ex, long convId)
        => conn.SendAsync(JsonSerializer.Serialize(
            new { type = "error", op, code = ex.Code, msg = ex.Message, conversationId = convId }, Json));

    private static async Task HandleReadAsync(ChatConnection conn, ChatService chat, JsonElement root)
    {
        if (!TryGetLong(root, "conversationId", out var convId)) return;
        TryGetLong(root, "lastReadMessageId", out var lastRead);
        // 与 send/recall 一致回执: 静默吞掉时客户端仍显示"已读", 双方状态不一致且无从排查
        try { await chat.MarkReadAsync(conn.UserId, convId, lastRead); }
        catch (BizException ex) { await SendOpErrorAsync(conn, "read", ex, convId); }
    }

    private static async Task HandleDeliveredAsync(ChatConnection conn, ChatService chat, JsonElement root)
    {
        if (!TryGetLong(root, "conversationId", out var convId)) return;
        TryGetLong(root, "lastDeliveredMessageId", out var lastDelivered);
        try { await chat.MarkDeliveredAsync(conn.UserId, convId, lastDelivered); }
        catch (BizException ex) { await SendOpErrorAsync(conn, "delivered", ex, convId); }
    }

    private static async Task HandleTypingAsync(ChatConnection conn, ChatService chat, JsonElement root)
    {
        if (!TryGetLong(root, "conversationId", out var convId)) return;
        var typing = !root.TryGetProperty("typing", out var t) || t.ValueKind != JsonValueKind.False;
        try { await chat.TypingAsync(conn.UserId, convId, typing); }
        catch (BizException ex) { await SendOpErrorAsync(conn, "typing", ex, convId); }
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

    private static (long UserId, DateTime ExpiresAtUtc)? ResolveUserId(HttpContext ctx, IConfiguration cfg)
    {
        // 头优先于 query: 新客户端把 token 放在 Authorization 里, query 只作为旧客户端兜底。
        // query 传 token 会被 ASP.NET 的 Request starting/finished 按 Information 级别整条打进
        // 日志(含 query 串), 7 天有效的 JWT 与其中的手机号 claim 会落到日志系统与代理访问日志。
        var token = ctx.Request.Headers["X-Token"].ToString();
        if (string.IsNullOrEmpty(token))
        {
            var auth = ctx.Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = auth["Bearer ".Length..].Trim();
        }
        if (string.IsNullOrEmpty(token))
            token = ctx.Request.Query["token"].FirstOrDefault();
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
            if (id == null || !long.TryParse(id, out var uid)) return null;
            var expRaw = principal.FindFirst("exp")?.Value;
            var expires = expRaw != null && long.TryParse(expRaw, out var exp)
                ? DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime
                : DateTime.UtcNow.AddHours(1);
            return (uid, expires);
        }
        catch { return null; }
    }

    /// <summary>账号是否仍可接收消息(被冻结后应停止推送)。</summary>
    private static async Task<bool> IsUserActiveAsync(IDbConnectionFactory db, long userId)
    {
        using var conn = db.Create();
        var status = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT status FROM sys_user WHERE id=@id", new { id = userId });
        return status == "ACTIVE";
    }

    /// <summary>单帧上限: 聊天消息正文 ≤2000 字符, 64KB 足够; 超限直接断开, 防止单连接把进程内存打满。</summary>
    private const int MaxFrameBytes = 64 * 1024;

    /// <summary>静默连接的最长复核间隔: 这段时间内没有收到任何帧也会回到主循环复核凭据与账号状态。</summary>
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 等待一帧文本。区分三种结果: 收到帧 / 对端关闭或帧超长(都应结束连接) / 空闲超时(应复核状态后继续等)。
    /// 空闲超时不能与"关闭"混为一谈, 否则要么静默客户端永远不被复核, 要么会被误断开。
    /// </summary>
    private enum ReceiveOutcome { Ok, Idle, TooLarge, Closed }

    private static async Task<(ReceiveOutcome Outcome, string? Text)> ReceiveTextOrIdleAsync(
        WebSocket socket, byte[] buffer, CancellationToken ct, TimeSpan idle)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(idle);
        try
        {
            var (text, tooLarge) = await ReceiveTextAsync(socket, buffer, linked.Token);
            if (tooLarge) return (ReceiveOutcome.TooLarge, null);
            return text == null ? (ReceiveOutcome.Closed, null) : (ReceiveOutcome.Ok, text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (ReceiveOutcome.Idle, null);
        }
    }

    private static async Task<(string? Text, bool TooLarge)> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return (null, false);
            // 超长必须与"对端关闭"区分开: 否则客户端收到的是 1000/bye, 会当成正常结束重连,
            // 消息被静默丢弃且没有任何错误提示, 排查时只能翻服务端日志。
            if (ms.Length + result.Count > MaxFrameBytes) return (null, true);
            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return (Encoding.UTF8.GetString(ms.ToArray()), false);
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
