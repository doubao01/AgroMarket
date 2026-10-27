using Microsoft.Extensions.Caching.Memory;

namespace AgroMarket.Api.Common;

/// <summary>
/// 匿名公开读接口的频次限制。
///
/// 为什么需要: 供需列表本身允许匿名访问(小程序分享页、H5 未登录浏览都要用),
/// 但"允许匿名"不等于"允许机器无限翻页"。之前既没有鉴权也没有频控,
/// 配合深翻页就能把一个县的供需连同乡镇位置整批搬走。
///
/// 做法: 按 IP 每分钟计数(单进程内存)。限制给得比较宽 —— 正常人翻列表远达不到,
/// 只挡住脚本级抓取; 带 token 的请求不计入, 登录用户不受影响。
/// 多实例部署时应换成共享计数(Redis), 否则限额只在单节点生效。
/// </summary>
public sealed class AnonymousReadThrottle
{
    private const int MaxPerMinute = 90;

    private readonly IMemoryCache _cache;
    private readonly object _gate = new();

    public AnonymousReadThrottle(IMemoryCache cache) => _cache = cache;

    /// <summary>返回 true 表示本次请求应被拒绝。</summary>
    public bool ShouldReject(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return false;
        // 按分钟分桶, 天然滑动: key 变化即重新计数, 无需定时清理
        var key = $"anon_read:{ip}:{DateTime.UtcNow:yyyyMMddHHmm}";
        lock (_gate)
        {
            var n = _cache.TryGetValue(key, out int cur) ? cur + 1 : 1;
            _cache.Set(key, n, TimeSpan.FromMinutes(2));
            return n > MaxPerMinute;
        }
    }
}
