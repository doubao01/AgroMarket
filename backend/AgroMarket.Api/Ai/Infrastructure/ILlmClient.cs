using AgroMarket.Api.Ai.Domain;

namespace AgroMarket.Api.Ai.Infrastructure;

/// <summary>流式响应分片。</summary>
public record LlmStreamChunk(
    string? ContentDelta,
    IReadOnlyList<ToolCall>? ToolCalls,
    bool IsDone,
    int PromptTokens = 0,
    int CompletionTokens = 0);

/// <summary>LLM 客户端抽象。多供应商只需替换实现 (OpenAI 兼容协议)。</summary>
public interface ILlmClient
{
    /// <summary>当前是否已配置可用。</summary>
    bool IsConfigured { get; }

    /// <summary>发起一次对话补全。未配置时抛出 501。</summary>
    Task<LlmResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken ct = default);

    /// <summary>流式补全。未配置时抛出 501。</summary>
    IAsyncEnumerable<LlmStreamChunk> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken ct = default);
}
