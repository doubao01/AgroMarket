using System.Text.Json;
using AgroMarket.Api.Ai.Domain;

namespace AgroMarket.Api.Ai.Tools;

/// <summary>AI 可调用工具契约。实现须为只读/受控, 严禁任意 SQL、下单、支付、跨用户隐私。</summary>
public interface IAiTool
{
    ToolDefinition Definition { get; }

    /// <summary>执行工具, 返回给 LLM 的字符串结果 (通常为 JSON)。</summary>
    Task<string> InvokeAsync(long userId, string argumentsJson, CancellationToken ct);
}

/// <summary>AI 工具注册表: 集中注册/发现, 并做名称白名单校验。</summary>
public class AiToolRegistry
{
    private readonly Dictionary<string, IAiTool> _tools;

    public AiToolRegistry(IEnumerable<IAiTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Definition.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<ToolDefinition> Definitions => _tools.Values.Select(t => t.Definition).ToList();

    public bool TryGet(string name, out IAiTool tool) => _tools.TryGetValue(name, out tool!);

    /// <summary>解析参数 JSON, 失败时返回空对象。</summary>
    public static JsonElement ParseArgs(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return doc.RootElement.Clone();
        }
        catch
        {
            using var doc = JsonDocument.Parse("{}");
            return doc.RootElement.Clone();
        }
    }
}
