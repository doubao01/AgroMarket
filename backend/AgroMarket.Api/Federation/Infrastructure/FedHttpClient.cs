using System.Net.Http.Json;
using System.Text.Json;
using AgroMarket.Api.Common;
using AgroMarket.Api.Federation.Dtos;

namespace AgroMarket.Api.Federation.Infrastructure;

/// <summary>调用对端节点的 HTTP 客户端 (节点间接口)。BaseUrl 指对端根地址 (不含 /api)。</summary>
public class FedHttpClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly ILogger<FedHttpClient> _logger;

    public FedHttpClient(HttpClient http, ILogger<FedHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    private static string Join(string baseUrl, string path) => baseUrl.TrimEnd('/') + path;

    /// <summary>向对端推送一批上报数据。</summary>
    public async Task<FedExportDto?> PushAsync(string baseUrl, string nodeToken, FedReportPayload payload, CancellationToken ct = default)
    {
        var url = Join(baseUrl, "/api/federation/node/reports");
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, options: JsonOpts),
        };
        req.Headers.TryAddWithoutValidation("X-Node-Token", nodeToken);
        using var resp = await _http.SendAsync(req, ct);
        return await ReadEnvelopeAsync<FedExportDto>(resp, url, ct);
    }

    /// <summary>从对端增量拉取。</summary>
    public async Task<FedExportDto?> PullAsync(string baseUrl, string nodeToken, DateTime since, int limit, CancellationToken ct = default)
    {
        var url = Join(baseUrl, $"/api/federation/node/export?since={Uri.EscapeDataString(since.ToString("yyyy-MM-dd HH:mm:ss"))}&limit={limit}");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("X-Node-Token", nodeToken);
        using var resp = await _http.SendAsync(req, ct);
        return await ReadEnvelopeAsync<FedExportDto>(resp, url, ct);
    }

    private async Task<T?> ReadEnvelopeAsync<T>(HttpResponseMessage resp, string url, CancellationToken ct)
    {
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new BizException($"对端 {url} 返回 {(int)resp.StatusCode}: {Truncate(body)}", ErrorCodes.ServerError);
        }
        var env = await resp.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOpts, ct);
        if (env == null) throw new BizException($"对端 {url} 响应无法解析", ErrorCodes.ServerError);
        if (env.Code != 0) throw new BizException($"对端 {url} 返回错误 {env.Code}: {env.Msg}", ErrorCodes.ServerError);
        return env.Data;
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200];
}
