using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
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

    public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new BizException("AI 服务未配置", ErrorCodes.NotImplemented);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["messages"] = messages.Select(ToRequestMessage).ToArray(),
            ["temperature"] = _options.Temperature,
            ["max_tokens"] = _options.MaxTokens,
            ["stream"] = true,
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
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
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

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var toolCallBuffers = new Dictionary<int, (string? id, StringBuilder name, StringBuilder args)>();

        // 只靠 ReadLineAsync 返回 null 判断结束: EndOfStream 是同步阻塞检查,
        // 在每个 SSE 分片的空闲期会占住线程池线程, 且与随后的异步读取重复
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data:")) continue;

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                yield return new LlmStreamChunk(null, null, IsDone: true);
                yield break;
            }

            JsonDocument doc;
            try { doc = JsonDocument.Parse(data); } catch { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;

                var choice = choices[0];
                var finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;

                if (choice.TryGetProperty("delta", out var delta))
                {
                    if (delta.TryGetProperty("content", out var contentElem) && contentElem.GetString() is { Length: > 0 } text)
                    {
                        yield return new LlmStreamChunk(text, null, IsDone: false);
                    }

                    if (delta.TryGetProperty("tool_calls", out var toolCallsElem) && toolCallsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in toolCallsElem.EnumerateArray())
                        {
                            var idx = tc.TryGetProperty("index", out var idxElem) ? idxElem.GetInt32() : 0;
                            if (!toolCallBuffers.TryGetValue(idx, out var buf))
                            {
                                buf = (null, new StringBuilder(), new StringBuilder());
                                toolCallBuffers[idx] = buf;
                            }
                            if (tc.TryGetProperty("id", out var idElem) && idElem.GetString() is { Length: > 0 } idStr)
                            {
                                toolCallBuffers[idx] = (idStr, buf.name, buf.args);
                            }
                            if (tc.TryGetProperty("function", out var fnElem))
                            {
                                if (fnElem.TryGetProperty("name", out var fnName) && fnName.GetString() is { Length: > 0 } nStr)
                                    buf.name.Append(nStr);
                                if (fnElem.TryGetProperty("arguments", out var fnArgs) && fnArgs.GetString() is { Length: > 0 } aStr)
                                    buf.args.Append(aStr);
                            }
                        }
                    }
                }

                if (finishReason == "tool_calls")
                {
                    var completedCalls = toolCallBuffers.OrderBy(kv => kv.Key).Select(kv => new ToolCall(
                        kv.Value.id ?? Guid.NewGuid().ToString("N"),
                        kv.Value.name.ToString(),
                        kv.Value.args.Length > 0 ? kv.Value.args.ToString() : "{}"
                    )).ToList();

                    yield return new LlmStreamChunk(null, completedCalls, IsDone: true);
                    yield break;
                }
                if (finishReason == "stop")
                {
                    yield return new LlmStreamChunk(null, null, IsDone: true);
                    yield break;
                }
            }
        }
    }

    private static object ToRequestMessage(ChatMessage m)
    {
        var role = m.Role.ToString().ToLowerInvariant();
        if (m.Role == ChatRole.Assistant && m.ToolCalls is { Count: > 0 })
        {
            return new Dictionary<string, object?>
            {
                ["role"] = role,
                ["content"] = string.IsNullOrEmpty(m.Content) ? null : m.Content,
                ["tool_calls"] = m.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.ArgumentsJson },
                }).ToArray(),
            };
        }
        if (m.Role == ChatRole.Tool)
        {
            return new Dictionary<string, object?>
            {
                ["role"] = role,
                ["content"] = m.Content,
                ["tool_call_id"] = m.ToolCallId,
                ["name"] = m.Name,
            };
        }
        return new Dictionary<string, object?> { ["role"] = role, ["content"] = m.Content };
    }

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
