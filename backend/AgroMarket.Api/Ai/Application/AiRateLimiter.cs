using AgroMarket.Api.Common;
using Microsoft.Extensions.Caching.Memory;

namespace AgroMarket.Api.Ai.Application;

/// <summary>AI 端点内存频控 (单进程, 按用户每分钟计数)。多实例部署时应替换为 Redis 计数。</summary>
public class AiRateLimiter
{
    private readonly IMemoryCache _cache;

    public AiRateLimiter(IMemoryCache cache) => _cache = cache;

    /// <summary>计数 +1; 超过阈值抛 409。窗口为滚动 1 分钟 (固定窗口)。</summary>
    public void Check(long userId, int perMinute)
    {
        var key = $"ai_rl_{userId}";
        var count = _cache.GetOrCreate(key, e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return 0;
        });
        if (count >= perMinute)
            throw new BizException("操作过于频繁, 请稍后再试", ErrorCodes.Conflict);
        _cache.Set(key, count + 1, TimeSpan.FromMinutes(1));
    }

    /// <summary>校验输入长度上限。</summary>
    public static void EnsureLength(string? text, int max, string field)
    {
        if (text is not null && text.Length > max)
            throw new BizException($"{field}过长 (最多 {max} 字)");
    }
}
