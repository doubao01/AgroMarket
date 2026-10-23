using Microsoft.Extensions.Caching.Memory;

namespace AgroMarket.Api.Common;

/// <summary>
/// 登录失败频控 (单进程内存计数): 阻止针对同一手机号的在线口令爆破。
/// 多实例部署时应替换为 Redis 计数, 否则锁定只在单节点生效。
/// </summary>
public class LoginThrottle
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly IMemoryCache _cache;
    private readonly object _gate = new();

    public LoginThrottle(IMemoryCache cache) => _cache = cache;

    public void EnsureAllowed(string phone)
    {
        if (_cache.TryGetValue(Key(phone), out int failures) && failures >= MaxFailures)
            throw new BizException("密码错误次数过多, 请 15 分钟后再试", ErrorCodes.TooManyRequests);
    }

    public void OnFailure(string phone)
    {
        lock (_gate)
        {
            var key = Key(phone);
            var n = _cache.TryGetValue(key, out int cur) ? cur + 1 : 1;
            // 每次失败都重置窗口: 连续爆破时锁定不会因为早期计数过期而自动解锁
            _cache.Set(key, n, Window);
        }
    }

    public void OnSuccess(string phone) => _cache.Remove(Key(phone));

    private static string Key(string phone) => "login_fail:" + phone;
}
