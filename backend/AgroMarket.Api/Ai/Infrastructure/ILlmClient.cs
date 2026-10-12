using AgroMarket.Api.Ai.Domain;

namespace AgroMarket.Api.Ai.Infrastructure;

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
}
