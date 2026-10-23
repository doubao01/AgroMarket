namespace AgroMarket.Api.Ai.Domain;

/// <summary>对话角色。</summary>
public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool,
}

/// <summary>一条对话消息 (OpenAI 兼容协议的映射)。助手消息可携带 tool_calls; 工具消息必须携带 tool_call_id。</summary>
public record ChatMessage(
    ChatRole Role,
    string Content,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? ToolCallId = null,
    string? Name = null)
{
    public static ChatMessage System(string content) => new(ChatRole.System, content);
    public static ChatMessage User(string content) => new(ChatRole.User, content);
    public static ChatMessage Assistant(string content) => new(ChatRole.Assistant, content);

    /// <summary>携带工具调用请求的助手消息, 下一轮须原样回传以符合 OpenAI 协议。</summary>
    // 带 tool_calls 的助手消息没有正文, 协议侧以空串占位(而不是 null), 避免序列化出 content:null
    public static ChatMessage Assistant(string? content, IReadOnlyList<ToolCall> toolCalls)
        => new(ChatRole.Assistant, content ?? "", toolCalls);

    /// <summary>工具执行结果消息, 必须回传对应 tool_call_id。</summary>
    public static ChatMessage Tool(string toolCallId, string name, string content)
        => new(ChatRole.Tool, content, ToolCallId: toolCallId, Name: name);
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
