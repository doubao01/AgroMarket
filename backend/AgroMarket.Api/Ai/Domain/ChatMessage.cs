namespace AgroMarket.Api.Ai.Domain;

/// <summary>对话角色。</summary>
public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool,
}

/// <summary>一条对话消息 (OpenAI 兼容协议的最小映射)。</summary>
public record ChatMessage(ChatRole Role, string Content)
{
    public static ChatMessage System(string content) => new(ChatRole.System, content);
    public static ChatMessage User(string content) => new(ChatRole.User, content);
    public static ChatMessage Assistant(string content) => new(ChatRole.Assistant, content);
    public static ChatMessage Tool(string content) => new(ChatRole.Tool, content);
}

/// <summary>LLM 可调用的工具定义 (JSON Schema 参数)。</summary>
public record ToolDefinition(string Name, string Description, string ParametersJsonSchema);

/// <summary>LLM 返回的一次工具调用请求。</summary>
public record ToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>一次 LLM 调用的结果。</summary>
public record LlmResponse(
    string Content,
    IReadOnlyList<ToolCall> ToolCalls,
    int PromptTokens,
    int CompletionTokens)
{
    public bool HasToolCalls => ToolCalls.Count > 0;
    public int TotalTokens => PromptTokens + CompletionTokens;
}
