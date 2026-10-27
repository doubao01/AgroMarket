using System.Security.Cryptography;
using System.Text;

namespace AgroMarket.Api.Services.Sms;

/// <summary>短信发送结果: Ok=false 时 Error 给出可读原因(不要泄漏账号信息)。</summary>
public record SmsSendResult(bool Ok, string? Error = null);

public interface ISmsSender
{
    /// <summary>渠道是否可用(密钥齐备)。不可用时 SendAsync 返回失败而不是抛异常。</summary>
    bool IsAvailable { get; }

    Task<SmsSendResult> SendAsync(string phone, string code, CancellationToken ct = default);
}

/// <summary>
/// 开发用: 只打印到日志与标准输出, 不真正发短信。
/// Program.cs 已保证 Sms:DevMode 在非 Development 环境会直接拒绝启动。
/// </summary>
public class DevSmsSender : ISmsSender
{
    private readonly ILogger<DevSmsSender> _logger;
    public DevSmsSender(ILogger<DevSmsSender> logger) => _logger = logger;

    public bool IsAvailable => true;

    public Task<SmsSendResult> SendAsync(string phone, string code, CancellationToken ct = default)
    {
        // 手机号脱敏后再打日志: 开发日志常常被贴进 issue/群聊, 完整号码没必要出现
        var masked = phone.Length >= 7 ? phone[..3] + "****" + phone[^4..] : "****";
        _logger.LogInformation("[DEV-SMS] 验证码已生成 phone={Phone} code={Code}", masked, code);
        Console.WriteLine($"[DEV-SMS] {masked} 的登录验证码: {code}");
        return Task.FromResult(new SmsSendResult(true));
    }
}

/// <summary>
/// 阿里云短信(OpenAPI RPC 风格, 签名 v1: HMAC-SHA1, 不依赖官方 SDK)。
///
/// 需要配置:
///   Sms__Provider=Aliyun
///   Sms__Aliyun__AccessKeyId / AccessKeySecret
///   Sms__Aliyun__SignName / TemplateCode
///
/// 签名算法: 参数按字典序拼接 canonicalized query -> StringToSign = "GET&%2F&" + percentEncode(canonicalized)
/// -> Signature = Base64(HMAC-SHA1(AccessKeySecret + "&", StringToSign))。
/// 注意: 本实现按阿里云官方文档编写, 但未接入真实账号做过联调, 上线前需用真实 AccessKey 验证一次。
/// </summary>
public class AliyunSmsSender : ISmsSender
{
    private const string Endpoint = "https://dysmsapi.aliyuncs.com/";

    private readonly string _keyId;
    private readonly string _keySecret;
    private readonly string _signName;
    private readonly string _templateCode;
    private readonly HttpClient _http;
    private readonly ILogger<AliyunSmsSender> _logger;

    public AliyunSmsSender(IConfiguration cfg, HttpClient http, ILogger<AliyunSmsSender> logger)
    {
        _keyId = cfg["Sms:Aliyun:AccessKeyId"] ?? "";
        _keySecret = cfg["Sms:Aliyun:AccessKeySecret"] ?? "";
        _signName = cfg["Sms:Aliyun:SignName"] ?? "";
        _templateCode = cfg["Sms:Aliyun:TemplateCode"] ?? "";
        _http = http;
        _logger = logger;
    }

    public bool IsAvailable =>
        _keyId.Length > 0 && _keySecret.Length > 0 && _signName.Length > 0 && _templateCode.Length > 0;

    public async Task<SmsSendResult> SendAsync(string phone, string code, CancellationToken ct = default)
    {
        if (!IsAvailable)
            return new SmsSendResult(false, "短信渠道未配置完整(需要 AccessKeyId/AccessKeySecret/SignName/TemplateCode)");

        var p = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["AccessKeyId"] = _keyId,
            ["Action"] = "SendSms",
            ["Format"] = "JSON",
            ["PhoneNumbers"] = phone,
            ["RegionId"] = "cn-hangzhou",
            ["SignName"] = _signName,
            ["SignatureMethod"] = "HMAC-SHA1",
            ["SignatureNonce"] = Guid.NewGuid().ToString("N"),
            ["SignatureVersion"] = "1.0",
            ["TemplateCode"] = _templateCode,
            ["TemplateParam"] = $"{{\"code\":\"{code}\"}}",
            ["Timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["Version"] = "2017-05-25",
        };

        var canonical = string.Join("&", p.Select(kv => $"{Pct(kv.Key)}={Pct(kv.Value)}"));
        var stringToSign = "GET&%2F&" + Pct(canonical);
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_keySecret + "&"));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
        var url = $"{Endpoint}?Signature={Pct(signature)}&{canonical}";

        try
        {
            using var resp = await _http.GetAsync(url, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            // 成功返回 {"Code":"OK",...}; 失败返回具体 Code/Message
            if (body.Contains("\"Code\":\"OK\"", StringComparison.Ordinal))
                return new SmsSendResult(true);

            _logger.LogWarning("aliyun sms failed phone={Phone} status={Status} body={Body}",
                phone, (int)resp.StatusCode, body.Length > 400 ? body[..400] : body);
            return new SmsSendResult(false, "短信发送失败, 请稍后重试");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "aliyun sms request error phone={Phone}", phone);
            return new SmsSendResult(false, "短信服务暂时不可用, 请稍后重试");
        }
    }

    /// <summary>阿里云要求的 percent-encode: 除 A-Za-z0-9-_.~ 外全部转义, 空格为 %20。</summary>
    private static string Pct(string s)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(s))
        {
            var c = (char)b;
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
                c == '-' || c == '_' || c == '.' || c == '~')
                sb.Append(c);
            else
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}

/// <summary>生产环境未配置任何短信渠道时使用: 明确报错, 不伪造发送成功。</summary>
public class UnconfiguredSmsSender : ISmsSender
{
    public bool IsAvailable => false;
    public Task<SmsSendResult> SendAsync(string phone, string code, CancellationToken ct = default)
        => Task.FromResult(new SmsSendResult(false, "短信服务未配置, 请联系管理员"));
}
