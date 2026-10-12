using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace AgroMarket.Api.Chat.Realtime;

/// <summary>单条 WebSocket 连接 (同一用户可有多条)。发送带锁, 避免并发写帧。</summary>
public class ChatConnection
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    public Guid Id { get; }
    public long UserId { get; }
    public WebSocket Socket { get; }

    public ChatConnection(Guid id, long userId, WebSocket socket)
    {
        Id = id; UserId = userId; Socket = socket;
    }

    public bool IsOpen => Socket.State == WebSocketState.Open;

    public async Task SendAsync(string json)
    {
        if (!IsOpen) return;
        var bytes = Encoding.UTF8.GetBytes(json);
        await _lock.WaitAsync();
        try
        {
            if (IsOpen)
                await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { /* 连接已断开, 忽略 */ }
        finally { _lock.Release(); }
    }
}

/// <summary>在线连接注册表 (单进程内存实现; 多实例部署时替换为 Redis 广播)。</summary>
public class ChatConnectionManager
{
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, ChatConnection>> _byUser = new();

    public ChatConnection Add(long userId, WebSocket socket)
    {
        var conn = new ChatConnection(Guid.NewGuid(), userId, socket);
        _byUser.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, ChatConnection>()).TryAdd(conn.Id, conn);
        return conn;
    }

    public void Remove(ChatConnection conn)
    {
        if (_byUser.TryGetValue(conn.UserId, out var set))
        {
            set.TryRemove(conn.Id, out _);
            if (set.IsEmpty) _byUser.TryRemove(conn.UserId, out _);
        }
    }

    public IReadOnlyList<ChatConnection> Get(long userId)
        => _byUser.TryGetValue(userId, out var set) ? set.Values.ToList() : Array.Empty<ChatConnection>();

    public bool IsOnline(long userId) => _byUser.TryGetValue(userId, out var s) && !s.IsEmpty;
}
