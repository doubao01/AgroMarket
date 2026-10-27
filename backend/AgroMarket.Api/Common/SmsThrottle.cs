using Microsoft.Extensions.Caching.Memory;

namespace AgroMarket.Api.Common;

/// <summary>
/// 验证码校验与发送的补充频控。
///
/// 已有护栏(见 AuthService.SendSmsCodeAsync): 同一手机号 60 秒 1 条、每小时 5 条。
/// 这里补上两处缺口:
///  1) 校验没有次数限制 —— 6 位数字在 10 分钟有效期内是可以被脚本枚举的,
///     一旦猜中就等于拿到了登录凭据。给每个手机号 5 次机会, 超了就作废本次验证码;
///  2) 发送只按手机号限频, 换个号码就能接着打 —— 攻击者轮换号码时短信费用没有上限。
///     按 IP 再加一层总量限制。
///
/// 单进程内存计数, 与 LoginThrottle 同款取舍(多实例部署需换共享存储)。
/// </summary>
public sealed class SmsThrottle
{
    private const int MaxVerifyAttempts = 5;
    private const int MaxSendPerIpPerHour = 20;

    private readonly IMemoryCache _cache;
    private readonly object _gate = new();

    public SmsThrottle(IMemoryCache cache) => _cache = cache;

    public void EnsureCanVerify(string phone)
    {
        if (_cache.TryGetValue(VerifyKey(phone), out int n) && n >= MaxVerifyAttempts)
            throw new BizException("验证码错误次数过多, 请重新获取验证码", ErrorCodes.TooManyRequests);
    }

    public void OnVerifyFailed(string phone)
    {
        lock (_gate)
        {
            var key = VerifyKey(phone);
            var n = _cache.TryGetValue(key, out int cur) ? cur + 1 : 1;
            _cache.Set(key, n, TimeSpan.FromMinutes(15));
        }
    }

    public void OnVerifySucceeded(string phone) => _cache.Remove(VerifyKey(phone));

    /// <summary>检查并占用一次该 IP 的发送配额(先检查后发送, 避免超额请求打到短信通道)。</summary>
    public void AcquireSendSlot(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return;
        lock (_gate)
        {
            var key = $"sms_send:{ip}:{DateTime.UtcNow:yyyyMMddHH}";
            var n = _cache.TryGetValue(key, out int cur) ? cur + 1 : 1;
            _cache.Set(key, n, TimeSpan.FromHours(2));
            if (n > MaxSendPerIpPerHour)
                throw new BizException("当前网络发送验证码次数过多, 请稍后再试", ErrorCodes.TooManyRequests);
        }
    }

    private static string VerifyKey(string phone) => "sms_verify:" + phone;
}
