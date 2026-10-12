namespace AgroMarket.Api.Chat.Domain;

/// <summary>消息类型 (chat_message.message_type)。</summary>
public static class ChatMessageType
{
    public const string Text = "TEXT";
    public const string Image = "IMAGE";
    public const string File = "FILE";
    public const string ProductCard = "PRODUCT_CARD";
    public const string Inquiry = "INQUIRY";
    public const string Quote = "QUOTE";
    public const string OrderCard = "ORDER_CARD";
    public const string System = "SYSTEM";

    /// <summary>P1 阶段允许客户端主动发送的类型; 其余由业务事件生成。</summary>
    public static readonly string[] ClientSendable = { Text, Image };

    /// <summary>消息可撤回时间窗(分钟), 与微信普通消息一致。</summary>
    public const int RecallWindowMinutes = 2;

    public static bool IsValid(string? type) => type is not null && new[]
    {
        Text, Image, File, ProductCard, Inquiry, Quote, OrderCard, System,
    }.Contains(type);
}

/// <summary>会话来源类型 (chat_conversation.source_type)。</summary>
public static class ChatSourceType
{
    public const string Product = "PRODUCT";
    public const string Supply = "SUPPLY";
    public const string Demand = "DEMAND";
    public const string Supplier = "SUPPLIER";

    public static bool IsValid(string? type) => type is null || new[]
    {
        Product, Supply, Demand, Supplier,
    }.Contains(type);
}

/// <summary>会话列表筛选。</summary>
public static class ChatFilter
{
    public const string All = "ALL";
    public const string Unread = "UNREAD";
}
