namespace AgroMarket.Api.Ai.Dtos;

/// <summary>AI 对话请求。</summary>
public record AiChatRequest(string Message, string? SessionId);

/// <summary>AI 对话响应。</summary>
public record AiChatResponse(string Reply, string SessionId, List<string> ToolsUsed, int TotalTokens);

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
