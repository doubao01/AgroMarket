using Dapper;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgroMarket.Api.Notifications;

/// <summary>
/// 微信小程序订阅消息推送。未配置 WeChat:AppId 时整体降级关闭 (IsConfigured=false),
/// 与 Ai 模块一致: 不伪造发送结果, 调用方按 SKIPPED 记录。
/// 发送为异步尽力送达, 失败仅记 push_log, 不影响业务。
/// </summary>
public class WeChatPushService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _cfg;
    private readonly ILogger<WeChatPushService> _logger;
    private readonly IDbConnectionFactory _db;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private volatile string? _accessToken;
    private DateTime _tokenExpiresAt;

    public WeChatPushService(IHttpClientFactory http, IConfiguration cfg, ILogger<WeChatPushService> logger, IDbConnectionFactory db)
    { _http = http; _cfg = cfg; _logger = logger; _db = db; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_cfg["WeChat:AppId"]) && !string.IsNullOrWhiteSpace(_cfg["WeChat:AppSecret"]);

    private string? TemplateOf(string key) => _cfg[$"WeChat:Templates:{key}"];

    /// <summary>发送订阅消息。返回发送状态, 结果落 push_log。额度在微信侧一次性消耗, 本地计数仅作跳过优化。</summary>
    public async Task<string> SendSubscribeAsync(long receiverId, string templateKey, string page, Dictionary<string, string> data)
    {
        var templateId = TemplateOf(templateKey);
        if (!IsConfigured || templateId == null)
        {
            await LogAsync(null, "WECHAT", receiverId, templateKey, "SKIPPED", "WeChat 未配置");
            return "SKIPPED";
        }
        try
        {
            // 订阅消息需要 openid; 用户未绑定 openid 视为 SKIPPED
            var openid = await GetOpenIdAsync(receiverId);
            if (string.IsNullOrEmpty(openid))
            {
                await LogAsync(null, "WECHAT", receiverId, templateKey, "SKIPPED", "用户未绑定 openid");
                return "SKIPPED";
            }
            if (await GetQuotaAsync(receiverId, templateKey) <= 0)
            {
                await LogAsync(null, "WECHAT", receiverId, templateKey, "SKIPPED", "无剩余订阅额度");
                return "SKIPPED";
            }
            var token = await GetAccessTokenAsync();
            if (token == null)
            {
                await LogAsync(null, "WECHAT", receiverId, templateKey, "FAILED", "access_token 获取失败");
                return "FAILED";
            }
            var http = _http.CreateClient("wechat");
            var resp = await http.PostAsJsonAsync(
                $"https://api.weixin.qq.com/cgi-bin/message/subscribe/send?access_token={token}",
                new { touser = openid, template_id = templateId, page, data }, Json);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
            var errCode = body.TryGetProperty("errcode", out var ec) ? ec.GetInt32() : 0;
            var errMsg = body.TryGetProperty("errmsg", out var em) ? em.GetString() : "";
            if (errCode == 0)
            {
                await DecrQuotaAsync(receiverId, templateKey);
                await LogAsync(null, "WECHAT", receiverId, templateKey, "SENT", null);
                return "SENT";
            }
            // 43101 = 用户未订阅/额度耗尽: 清零本地计数避免反复请求
            if (errCode == 43101) await ClearQuotaAsync(receiverId, templateKey);
            await LogAsync(null, "WECHAT", receiverId, templateKey, errCode == 43101 ? "UNSUBSCRIBED" : "FAILED", $"{errCode}:{errMsg}");
            _logger.LogWarning("wechat subscribe send failed user={User} code={Code}", receiverId, errCode);
            return errCode == 43101 ? "UNSUBSCRIBED" : "FAILED";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "wechat subscribe send error user={User}", receiverId);
            try { await LogAsync(null, "WECHAT", receiverId, templateKey, "FAILED", ex.Message[..Math.Min(490, ex.Message.Length)]); } catch { }
            return "FAILED";
        }
    }

    /// <summary>用户上报订阅授权结果, 每次授权 +1 次额度 (一次性订阅消息)。</summary>
    public async Task ReportSubscribeQuotaAsync(long userId, string templateKey, bool accepted)
    {
        if (!accepted) return;
        using var conn = _db.Create();
        await conn.ExecuteAsync(@"
            INSERT INTO wx_subscribe_quota (user_id, template_key, quota) VALUES (@u, @k, 1)
            ON DUPLICATE KEY UPDATE quota = quota + 1",
            new { u = userId, k = templateKey });
    }

    private async Task<long> GetQuotaAsync(long userId, string templateKey)
        => await _db.Create().ExecuteScalarAsync<long>(
            "SELECT quota FROM wx_subscribe_quota WHERE user_id=@u AND template_key=@k", new { u = userId, k = templateKey });

    private async Task DecrQuotaAsync(long userId, string templateKey)
        => await _db.Create().ExecuteAsync(
            "UPDATE wx_subscribe_quota SET quota = GREATEST(quota-1, 0) WHERE user_id=@u AND template_key=@k", new { u = userId, k = templateKey });

    private async Task ClearQuotaAsync(long userId, string templateKey)
        => await _db.Create().ExecuteAsync(
            "UPDATE wx_subscribe_quota SET quota = 0 WHERE user_id=@u AND template_key=@k", new { u = userId, k = templateKey });

    private async Task<string?> GetOpenIdAsync(long userId)
    {
        // sys_user 无 openid 列时返回 null; 若后续接入微信登录, 在 sys_user 增加 wx_openid 列后此处自动生效
        try
        {
            return await _db.Create().ExecuteScalarAsync<string?>(
                "SELECT wx_openid FROM sys_user WHERE id=@u AND wx_openid IS NOT NULL", new { u = userId });
        }
        catch { return null; }
    }

    private async Task LogAsync(long? messageId, string channel, long receiverId, string? templateKey, string status, string? error)
    {
        try
        {
            using var conn = _db.Create();
            await conn.ExecuteAsync(
                "INSERT INTO push_log (message_id, channel, receiver_id, template_key, status, error) VALUES (@m, @c, @r, @k, @s, @e)",
                new { m = messageId, c = channel, r = receiverId, k = templateKey, s = status, e = error is null ? null : error[..Math.Min(490, error.Length)] });
        }
        catch { }
    }

    private async Task<string?> GetAccessTokenAsync()
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiresAt) return _accessToken;
        await _tokenLock.WaitAsync();
        try
        {
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiresAt) return _accessToken;
            var appId = _cfg["WeChat:AppId"];
            var secret = _cfg["WeChat:AppSecret"];
            var http = _http.CreateClient("wechat");
            var resp = await http.GetFromJsonAsync<JsonElement>(
                $"https://api.weixin.qq.com/cgi-bin/token?grant_type=client_credential&appid={appId}&secret={secret}", Json);
            if (resp.TryGetProperty("access_token", out var tk))
            {
                _accessToken = tk.GetString();
                var expiresIn = resp.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 7200;
                _tokenExpiresAt = DateTime.UtcNow.AddSeconds(Math.Max(300, expiresIn - 300));
                return _accessToken;
            }
            _logger.LogWarning("wechat token fetch failed: {Body}", resp.GetRawText());
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "wechat token fetch error");
            return null;
        }
        finally { _tokenLock.Release(); }
    }
}
