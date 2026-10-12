using System.Text.Json;
using AgroMarket.Api.Chat.Dtos;
using AgroMarket.Api.Chat.Infrastructure;

namespace AgroMarket.Api.Chat.Realtime;

/// <summary>实时推送抽象: 业务层只关心"通知", 不感知传输实现 (ADR-1)。</summary>
public interface IRealtimeNotifier
{
    Task NotifyMessageAsync(long conversationId, ChatMessageDto message, long senderId);
    Task NotifyReadAsync(long conversationId, long readerId, long lastReadMessageId);
    Task NotifyDeliveredAsync(long conversationId, long userId, long lastDeliveredMessageId);
    Task NotifyRecallAsync(long conversationId, long messageId, long senderId);
    Task NotifyTypingAsync(long conversationId, long userId, bool typing);
}

public class ChatRealtimeNotifier : IRealtimeNotifier
{
    private readonly ChatConnectionManager _mgr;
    private readonly ChatRepository _repo;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ChatRealtimeNotifier(ChatConnectionManager mgr, ChatRepository repo)
    {
        _mgr = mgr; _repo = repo;
    }

    public async Task NotifyMessageAsync(long conversationId, ChatMessageDto message, long senderId)
    {
        var ids = await _repo.GetParticipantIdsAsync(conversationId);
        var json = JsonSerializer.Serialize(new { type = "message", conversationId, message }, Json);
        foreach (var uid in ids) await PushAsync(uid, json);
    }

    public async Task NotifyReadAsync(long conversationId, long readerId, long lastReadMessageId)
    {
        var ids = await _repo.GetParticipantIdsAsync(conversationId);
        var json = JsonSerializer.Serialize(
            new { type = "read", conversationId, userId = readerId, lastReadMessageId }, Json);
        foreach (var uid in ids)
            if (uid != readerId) await PushAsync(uid, json);
    }

    public async Task NotifyDeliveredAsync(long conversationId, long userId, long lastDeliveredMessageId)
    {
        var ids = await _repo.GetParticipantIdsAsync(conversationId);
        var json = JsonSerializer.Serialize(
            new { type = "delivered", conversationId, userId, lastDeliveredMessageId }, Json);
        foreach (var uid in ids)
            if (uid != userId) await PushAsync(uid, json);
    }

    public async Task NotifyRecallAsync(long conversationId, long messageId, long senderId)
    {
        var ids = await _repo.GetParticipantIdsAsync(conversationId);
        var json = JsonSerializer.Serialize(
            new { type = "recall", conversationId, messageId, userId = senderId }, Json);
        foreach (var uid in ids) await PushAsync(uid, json);
    }

    public async Task NotifyTypingAsync(long conversationId, long userId, bool typing)
    {
        var ids = await _repo.GetParticipantIdsAsync(conversationId);
        var json = JsonSerializer.Serialize(
            new { type = "typing", conversationId, userId, typing }, Json);
        foreach (var uid in ids)
            if (uid != userId) await PushAsync(uid, json);
    }

    private async Task PushAsync(long userId, string json)
    {
        foreach (var conn in _mgr.Get(userId)) await conn.SendAsync(json);
    }
}
