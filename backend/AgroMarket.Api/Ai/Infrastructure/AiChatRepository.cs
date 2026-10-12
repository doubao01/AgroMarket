using System.Data;
using AgroMarket.Api.Data;
using AgroMarket.Api.Ai.Dtos;
using Dapper;

namespace AgroMarket.Api.Ai.Infrastructure;

// ---------------- Dapper 行映射类型 (属性类) ----------------

public class AiConversationRow
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public int MessageCount { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AiMessageRow
{
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ToolsUsed { get; set; }
    public int Tokens { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>AI 对话数据访问 (Dapper)。会话/消息持久化, 读取方法自开连接。</summary>
public class AiChatRepository
{
    private readonly IDbConnectionFactory _db;
    public AiChatRepository(IDbConnectionFactory db) => _db = db;

    private const string ConversationSelect = @"
SELECT id AS Id, user_id AS UserId, session_id AS SessionId, title AS Title,
       message_count AS MessageCount, last_message_at AS LastMessageAt, created_at AS CreatedAt
FROM ai_conversation";

    private const string MessageSelect = @"
SELECT id AS Id, conversation_id AS ConversationId, role AS Role, content AS Content,
       tools_used AS ToolsUsed, tokens AS Tokens, created_at AS CreatedAt
FROM ai_message";

    /// <summary>按 session_id 取会话(含归属), 用于校验与复用。</summary>
    public async Task<AiConversationRow?> GetBySessionAsync(string sessionId)
    {
        using var conn = _db.Create();
        return await conn.QuerySingleOrDefaultAsync<AiConversationRow>(
            ConversationSelect + " WHERE session_id=@s", new { s = sessionId });
    }

    public async Task<AiConversationRow> CreateAsync(long userId, string sessionId)
    {
        using var conn = _db.Create();
        var id = await conn.ExecuteScalarAsync<long>(
            "INSERT INTO ai_conversation (user_id, session_id) VALUES (@uid, @s); SELECT LAST_INSERT_ID();",
            new { uid = userId, s = sessionId });
        return new AiConversationRow { Id = id, UserId = userId, SessionId = sessionId, CreatedAt = DateTime.Now };
    }

    public async Task<List<AiConversationRow>> ListAsync(long userId, int limit, int offset)
    {
        using var conn = _db.Create();
        var rows = await conn.QueryAsync<AiConversationRow>(
            ConversationSelect + " WHERE user_id=@uid ORDER BY COALESCE(last_message_at, created_at) DESC, id DESC LIMIT @limit OFFSET @offset",
            new { uid = userId, limit, offset });
        return rows.ToList();
    }

    public async Task<long> CountAsync(long userId)
    {
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM ai_conversation WHERE user_id=@uid", new { uid = userId });
    }

    public async Task<AiConversationRow?> GetOwnedAsync(long convId, long userId)
    {
        using var conn = _db.Create();
        return await conn.QuerySingleOrDefaultAsync<AiConversationRow>(
            ConversationSelect + " WHERE id=@c AND user_id=@uid", new { c = convId, uid = userId });
    }

    /// <summary>取最近 N 条消息(升序返回, 供 LLM 记忆与展示)。</summary>
    public async Task<List<AiMessageRow>> GetRecentMessagesAsync(long convId, int limit)
    {
        using var conn = _db.Create();
        var rows = await conn.QueryAsync<AiMessageRow>(
            MessageSelect + " WHERE conversation_id=@c ORDER BY id DESC LIMIT @limit",
            new { c = convId, limit });
        var list = rows.ToList();
        list.Reverse();
        return list;
    }

    /// <summary>历史分页(游标 beforeId, 取更早消息, 升序返回)。limit+1 由调用方裁剪。</summary>
    public async Task<List<AiMessageRow>> ListMessagesAsync(long convId, long? beforeId, int limit)
    {
        using var conn = _db.Create();
        var where = beforeId is > 0 ? " AND id < @before" : "";
        var rows = await conn.QueryAsync<AiMessageRow>(
            MessageSelect + " WHERE conversation_id=@c" + where + " ORDER BY id DESC LIMIT @limit",
            new { c = convId, before = beforeId, limit });
        var list = rows.ToList();
        list.Reverse();
        return list;
    }

    public async Task<long> AppendMessageAsync(long convId, string role, string content, string? toolsUsed, int tokens)
    {
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO ai_message (conversation_id, role, content, tools_used, tokens)
              VALUES (@c, @role, @content, @tools, @tokens); SELECT LAST_INSERT_ID();",
            new { c = convId, role, content, tools = toolsUsed, tokens });
    }

    /// <summary>原子写入一轮对话(用户+助手)并刷新会话摘要。</summary>
    public async Task AppendTurnAsync(
        long convId, string userContent, string assistantContent, string? toolsJson, int tokens, string? titleIfNull)
    {
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            @"INSERT INTO ai_message (conversation_id, role, content) VALUES (@c, 'USER', @content)",
            new { c = convId, content = userContent }, tx);
        await conn.ExecuteAsync(
            @"INSERT INTO ai_message (conversation_id, role, content, tools_used, tokens)
              VALUES (@c, 'ASSISTANT', @content, @tools, @tokens)",
            new { c = convId, content = assistantContent, tools = toolsJson, tokens }, tx);
        await conn.ExecuteAsync(
            @"UPDATE ai_conversation
                 SET title = COALESCE(title, @title),
                     message_count = message_count + 2,
                     last_message_at = NOW(3)
               WHERE id=@c",
            new { c = convId, title = titleIfNull }, tx);
        tx.Commit();
    }

    /// <summary>刷新会话摘要: 仅在标题为空时写标题, 累加计数并更新最后消息时间。</summary>
    public async Task TouchAsync(long convId, string? titleIfNull, int deltaCount)
    {
        using var conn = _db.Create();
        await conn.ExecuteAsync(
            @"UPDATE ai_conversation
                 SET title = COALESCE(title, @title),
                     message_count = message_count + @delta,
                     last_message_at = NOW(3)
               WHERE id=@c",
            new { c = convId, title = titleIfNull, delta = deltaCount });
    }

    public async Task<bool> DeleteAsync(long convId, long userId)
    {
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        var owned = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM ai_conversation WHERE id=@c AND user_id=@uid",
            new { c = convId, uid = userId }, tx);
        if (owned == 0) { tx.Rollback(); return false; }
        await conn.ExecuteAsync("DELETE FROM ai_message WHERE conversation_id=@c", new { c = convId }, tx);
        await conn.ExecuteAsync("DELETE FROM ai_conversation WHERE id=@c", new { c = convId }, tx);
        tx.Commit();
        return true;
    }

    public async Task RenameAsync(long convId, long userId, string title)
    {
        using var conn = _db.Create();
        await conn.ExecuteAsync(
            "UPDATE ai_conversation SET title=@t WHERE id=@c AND user_id=@uid",
            new { c = convId, uid = userId, t = title });
    }

    // ---------------- 映射 ----------------

    public static AiConversationDto MapConversation(AiConversationRow r) => new(
        r.Id, r.SessionId, r.Title, r.MessageCount, r.LastMessageAt, r.CreatedAt);

    public static AiMessageDto MapMessage(AiMessageRow r) => new(
        r.Id, r.Role, r.Content, ParseTools(r.ToolsUsed), r.CreatedAt);

    private static List<string> ParseTools(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch { return new List<string>(); }
    }
}