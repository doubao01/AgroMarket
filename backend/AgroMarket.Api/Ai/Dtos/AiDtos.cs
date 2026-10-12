namespace AgroMarket.Api.Ai.Dtos;

/// <summary>AI 对话请求。</summary>
public record AiChatRequest(string Message, string? SessionId);

/// <summary>AI 对话响应。</summary>
public record AiChatResponse(string Reply, string SessionId, List<string> ToolsUsed, int TotalTokens);

/// <summary>AI 会话摘要。</summary>
public record AiConversationDto(
    long Id, string SessionId, string? Title, int MessageCount, DateTime? LastMessageAt, DateTime CreatedAt);

/// <summary>AI 会话列表。</summary>
public record AiConversationPageDto(List<AiConversationDto> Items, long Total);

/// <summary>AI 消息。</summary>
public record AiMessageDto(long Id, string Role, string Content, List<string> ToolsUsed, DateTime CreatedAt);

/// <summary>AI 消息分页。Items 按时间升序; NextCursor 为加载更早消息的游标(最旧消息ID)。</summary>
public record AiMessagePageDto(List<AiMessageDto> Items, bool HasMore, long? NextCursor);

/// <summary>重命名会话请求。</summary>
public record AiConversationRenameRequest(string Title);

/// <summary>内容发布辅助请求。</summary>
public record AiAssistRequest(
    string Type, string? Title, string? Category, string? Keywords, decimal? Quantity, decimal? PriceHint);

/// <summary>内容发布辅助响应。</summary>
public record AiAssistResponse(
    string Description, string SuggestedCategory, decimal? PriceMin, decimal? PriceMax, string? PriceUnit);

/// <summary>大屏智能解读响应。</summary>
public record AiInsightResponse(string Summary, List<string> Highlights);

/// <summary>大屏数据问答请求/响应。</summary>
public record AiAskRequest(string Question);
public record AiAskResponse(string Answer, List<string> Facts);
