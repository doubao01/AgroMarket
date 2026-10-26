using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace AgroMarket.Api.Chat.Realtime;

/// <summary>单条 WebSocket 连接 (同一用户可有多条)。发送带锁, 避免并发写帧。</summary>
public class ChatConnection
{
    // 写超时: 对端"连上但不读"(挂起的 App、被暂停的标签页)时, 内核发送缓冲迟早耗尽,
    // 此时 SendAsync 会无限挂起, 而调用它的是发消息的 HTTP 请求 —— 等于一个僵尸连接
    // 就能把发送方的请求线程一直占住。超时后直接 Abort, 让读循环抛错走 finally 摘除连接。
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);
    // 单连接待写字节上限: 超过说明对端已经不读了, 继续排队只是堆积内存。
    private const long MaxPendingBytes = 512 * 1024;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private long _pendingBytes;
    public Guid Id { get; }
    public long UserId { get; }
    public WebSocket Socket { get; }

    /// <summary>曾因积压而丢帧。客户端重连后应主动按 lastMessageId 补拉。</summary>
    public bool DroppedFrames { get; private set; }

    public ChatConnection(Guid id, long userId, WebSocket socket)
    {
        Id = id; UserId = userId; Socket = socket;
    }

    public bool IsOpen => Socket.State == WebSocketState.Open;

    /// <summary>发送一帧。返回 false 表示这一帧没送到(连接已断/超时/积压丢弃)。</summary>
    public async Task<bool> SendAsync(string json)
    {
        if (!IsOpen) return false;
        var bytes = Encoding.UTF8.GetBytes(json);

        if (Interlocked.Add(ref _pendingBytes, bytes.Length) > MaxPendingBytes)
        {
            Interlocked.Add(ref _pendingBytes, -bytes.Length);
            DroppedFrames = true;
            return false;
        }

        await _lock.WaitAsync();
        try
        {
            if (!IsOpen) return false;
            using var cts = new CancellationTokenSource(WriteTimeout);
            await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
            return true;
        }
        catch
        {
            // 超时或写失败都说明这条连接已经不可用: 主动 Abort,
            // 让收帧循环抛 WebSocketException 并走 finally 从注册表摘除。
            try { Socket.Abort(); } catch { }
            return false;
        }
        finally
        {
            Interlocked.Add(ref _pendingBytes, -bytes.Length);
            _lock.Release();
        }
    }
}

/// <summary>在线连接注册表 (单进程内存实现; 多实例部署时替换为 Redis 广播)。</summary>
public class ChatConnectionManager
{
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, ChatConnection>> _byUser = new();

    // Add 与 Remove 必须互斥: Remove 里"集合空了就摘掉用户"这一步, 若与 Add 交错,
    // 会把刚加进来的新连接连同用户条目一起摘掉 —— 该连接不再收到任何推送,
    // 但 socket 还活着, 客户端以为自己在线, 表现为"偶发收不到消息"。
    private readonly object _gate = new();

    public ChatConnection Add(long userId, WebSocket socket)
    {
        var conn = new ChatConnection(Guid.NewGuid(), userId, socket);
        lock (_gate)
        {
            var set = _byUser.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, ChatConnection>());
            set.TryAdd(conn.Id, conn);
        }
        return conn;
    }

    public void Remove(ChatConnection conn)
    {
        lock (_gate)
        {
            if (!_byUser.TryGetValue(conn.UserId, out var set)) return;
            set.TryRemove(conn.Id, out _);
            if (set.IsEmpty) _byUser.TryRemove(conn.UserId, out _);
        }
    }

    public IReadOnlyList<ChatConnection> Get(long userId)
        => _byUser.TryGetValue(userId, out var set) ? set.Values.ToList() : Array.Empty<ChatConnection>();

    public bool IsOnline(long userId) => _byUser.TryGetValue(userId, out var s) && !s.IsEmpty;

    /// <summary>
    /// 立即踢下线: 先整体摘除注册表(此后任何推送都投递不到该用户), 再关闭底层 socket。
    /// 用于冻结账号等需要"立刻生效"的场景 —— 只摘注册表也能止住推送, 但连接会挂到读循环
    /// 下一次回查状态才关闭, 客户端以为自己还在线。
    /// </summary>
    public async Task KickUserAsync(long userId, string reason)
    {
        ConcurrentDictionary<Guid, ChatConnection>? set;
        lock (_gate)
        {
            if (!_byUser.TryRemove(userId, out set)) return;
        }
        foreach (var conn in set.Values)
        {
            try
            {
                if (conn.Socket.State == WebSocketState.Open)
                    await conn.Socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, CancellationToken.None);
            }
            catch { /* 已经断开或正在断开 */ }
        }
    }
}
