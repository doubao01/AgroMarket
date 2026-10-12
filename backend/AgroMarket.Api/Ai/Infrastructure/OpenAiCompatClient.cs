using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgroMarket.Api.Ai.Domain;
using AgroMarket.Api.Common;
using Microsoft.Extensions.Options;

namespace AgroMarket.Api.Ai.Infrastructure;

/// <summary>OpenAI 兼容 /chat/completions 客户端 (适配 DeepSeek/通义/智谱等)。</summary>
public class OpenAiCompatClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly AiOptions _options;

    public OpenAiCompatClient(HttpClient http, IOptions<AiOptions> options)
    {
        _http = http;
        _options = options.Value;
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
            _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        if (_options.TimeoutSeconds > 0)
            _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new BizException("AI 服务未配置", ErrorCodes.NotImplemented);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["messages"] = messages.Select(ToRequestMessage).ToArray(),
            ["temperature"] = _options.Temperature,
            ["max_tokens"] = _options.MaxTokens,
        };
        if (tools is { Count: > 0 })
        {
            payload["tools"] = tools.Select(t => new
            {
                type = "function",
                function = new { name = t.Name, description = t.Description, parameters = ParseSchema(t.ParametersJsonSchema) },
            }).ToArray();
            payload["tool_choice"] = "auto";
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload),
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BizException("AI 服务响应超时", ErrorCodes.ServerError);
        }

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new BizException($"AI 服务调用失败({(int)resp.StatusCode}): {Truncate(body, 200)}", ErrorCodes.ServerError);
        }

        var parsed = await resp.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: ct)
            ?? throw new BizException("AI 服务返回为空", ErrorCodes.ServerError);
        var msg = parsed.Choices?.FirstOrDefault()?.Message
            ?? throw new BizException("AI 服务返回结构异常", ErrorCodes.ServerError);

        var toolCalls = (msg.ToolCalls ?? new List<ToolCallDto>())
            .Where(t => t.Function != null)
            .Select(t => new ToolCall(t.Id ?? Guid.NewGuid().ToString("N"), t.Function!.Name ?? "", t.Function.Arguments ?? "{}"))
            .ToList();

        return new LlmResponse(
            msg.Content ?? string.Empty,
            toolCalls,
            parsed.Usage?.PromptTokens ?? 0,
            parsed.Usage?.CompletionTokens ?? 0);
    }

    private static object ToRequestMessage(ChatMessage m) => new
    {
        role = m.Role.ToString().ToLowerInvariant(),
        content = m.Content,
    };

    private static JsonElement ParseSchema(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{\"type\":\"object\",\"properties\":{}}" : json);
        return doc.RootElement.Clone();
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "...";

    // ---- OpenAI 兼容响应 DTO ----
    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
        [JsonPropertyName("usage")] public Usage? Usage { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ResponseMessage? Message { get; set; }
    }

    private sealed class ResponseMessage
    {
        [JsonPropertyName("role")] public string? Role { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("tool_calls")] public List<ToolCallDto>? ToolCalls { get; set; }
    }

    private sealed class ToolCallDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("function")] public FunctionDto? Function { get; set; }
    }

    private sealed class FunctionDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("arguments")] public string? Arguments { get; set; }
    }

    private sealed class Usage
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
    }
}
