namespace AgroMarket.Api.Common;

/// <summary>
/// 按 key 分片的异步互斥锁。
///
/// 用途: 让"落库 + 广播"配对执行。并发发消息时, 消息 id 由自增分配、广播却是并行的,
/// 两个请求可能先拿到 67 后拿到 66, 于是推送顺序与 id 顺序相反 —— 客户端按到达顺序
/// 渲染就会出现气泡乱序, 刷新后又变正常(HTTP 按 id 排)。
///
/// 为什么用固定分片而不是"每个 key 一把锁": 后者需要按需创建并回收, 会话多了会持续增长;
/// 分片数固定则不泄漏, 代价是不同会话偶尔共享一把锁, 对聊天发送的吞吐影响可忽略。
/// </summary>
public sealed class StripedLock
{
    private readonly SemaphoreSlim[] _stripes;

    public StripedLock(int stripes = 64)
    {
        _stripes = new SemaphoreSlim[stripes];
        for (var i = 0; i < stripes; i++) _stripes[i] = new SemaphoreSlim(1, 1);
    }

    public async Task<IDisposable> LockAsync(long key)
    {
        var s = _stripes[(int)((ulong)key % (ulong)_stripes.Length)];
        await s.WaitAsync();
        return new Releaser(s);
    }

    private sealed class Releaser(SemaphoreSlim s) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            s.Release();
        }
    }
}
