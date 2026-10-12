using AgroMarket.Api.Chat.Dtos;
using AgroMarket.Api.Chat.Domain;
using AgroMarket.Api.Common;
using AgroMarket.Api.Dtos;
using AgroMarket.Api.Services;
using Dapper;

namespace AgroMarket.Api.Chat.Application;

/// <summary>
/// 会话内交易编排 (ADR-2)。把聊天与既有交易状态机桥接起来:
/// 分享商品卡片、会话内发起报价、从聊天直接下单, 并在关键节点向会话推送结构化消息。
/// 交易状态仍由 TradeService 状态机强制校验, 本服务不直接改订单状态。
/// </summary>
public class ChatTradeService
{
    private readonly ChatService _chat;
    private readonly TradeService _trade;
    private readonly IDbConnectionFactory _db;

    public ChatTradeService(ChatService chat, TradeService trade, IDbConnectionFactory db)
    {
        _chat = chat;
        _trade = trade;
        _db = db;
    }

    /// <summary>分享供应商品或采购需求到会话 (PRODUCT_CARD)。</summary>
    public async Task<ChatMessageDto> ShareProductAsync(long userId, long convId, ShareProductRequest req)
    {
        await _chat.EnsurePeerReachableAsync(userId, convId);
        if ((req.SupplyId == null) == (req.DemandId == null))
            throw new BizException("必须选择供应商品或采购需求其一");

        object payload;
        string content;
        string bizType;
        long bizId;

        using (var conn = _db.Create())
        {
            if (req.SupplyId != null)
            {
                var row = await conn.QueryFirstOrDefaultAsync<(long Id, string Title, string? Spec, string Unit, decimal? Price, string? Image, string Status)>(
                    @"SELECT s.id, s.title, s.spec, s.unit, s.price,
                             (SELECT i.url FROM product_image i WHERE i.supply_id = s.id ORDER BY i.sort_order, i.id LIMIT 1) Image,
                             s.status
                      FROM product_supply s WHERE s.id=@id",
                    new { id = req.SupplyId });
                if (row == default) throw new BizException("供应商品不存在", ErrorCodes.NotFound);
                if (row.Status != "ACTIVE") throw new BizException("该供应信息当前不可交易");
                bizType = "SUPPLY"; bizId = row.Id; content = $"[商品] {row.Title}";
                payload = new { kind = "SUPPLY", id = row.Id, title = row.Title, spec = row.Spec, unit = row.Unit, price = row.Price, image = row.Image };
            }
            else
            {
                var row = await conn.QueryFirstOrDefaultAsync<(long Id, string Title, string? Spec, string Unit, decimal? PriceMin, decimal? PriceMax, string Status)>(
                    "SELECT id, title, spec_required Spec, unit, price_min PriceMin, price_max PriceMax, status FROM purchase_demand WHERE id=@id",
                    new { id = req.DemandId });
                if (row == default) throw new BizException("采购需求不存在", ErrorCodes.NotFound);
                if (row.Status != "ACTIVE") throw new BizException("该采购需求当前不可交易");
                bizType = "DEMAND"; bizId = row.Id; content = $"[需求] {row.Title}";
                payload = new { kind = "DEMAND", id = row.Id, title = row.Title, spec = row.Spec, unit = row.Unit, priceMin = row.PriceMin, priceMax = row.PriceMax };
            }
        }

        return await _chat.SendBusinessAsync(userId, convId, ChatMessageType.ProductCard, content, payload, bizType, bizId);
    }

    /// <summary>会话内发起报价: 创建 trade_quote(挂 conversation_id) 并推送 QUOTE 卡片消息。</summary>
    public async Task<QuoteDto> CreateQuoteAsync(long userId, long convId, ChatQuoteRequest req)
    {
        await _chat.EnsurePeerReachableAsync(userId, convId);
        var quote = await _trade.CreateQuoteAsync(userId,
            new QuoteCreateRequest(req.SupplyId, req.DemandId, req.Price, req.PriceUnit, req.Quantity, req.Remark),
            convId);

        var title = quote.SupplyTitle ?? quote.DemandTitle ?? "交易信息";
        await PushQuoteCardAsync(convId, userId, quote, title, "报价");
        return quote;
    }

    /// <summary>会话内接受报价 (仅报价所属会话的成员)。</summary>
    public async Task<QuoteDto> AcceptQuoteAsync(long userId, long convId, long quoteId)
    {
        await _chat.EnsurePeerReachableAsync(userId, convId);
        await EnsureQuoteInConversationAsync(quoteId, convId);
        var quote = await _trade.AcceptQuoteAsync(userId, quoteId);
        await PushQuoteCardAsync(convId, userId, quote, TitleOf(quote), "已接受报价");
        return quote;
    }

    /// <summary>会话内拒绝报价。</summary>
    public async Task<QuoteDto> RejectQuoteAsync(long userId, long convId, long quoteId)
    {
        await _chat.EnsurePeerReachableAsync(userId, convId);
        await EnsureQuoteInConversationAsync(quoteId, convId);
        var quote = await _trade.RejectQuoteAsync(userId, quoteId);
        await PushQuoteCardAsync(convId, userId, quote, TitleOf(quote), "已拒绝报价");
        return quote;
    }

    /// <summary>会话内还价。</summary>
    public async Task<QuoteDto> CounterQuoteAsync(long userId, long convId, long quoteId, ChatQuoteCounterRequest req)
    {
        await _chat.EnsurePeerReachableAsync(userId, convId);
        await EnsureQuoteInConversationAsync(quoteId, convId);
        var quote = await _trade.CounterQuoteAsync(userId, quoteId,
            new QuoteCounterRequest(req.Price, req.Quantity, req.Remark));
        // 还价生成新报价, 关联到同一会话
        await _trade.LinkQuoteConversationAsync(quote.Id, convId);
        await PushQuoteCardAsync(convId, userId, quote, TitleOf(quote), "还价");
        return quote;
    }

    private static string TitleOf(QuoteDto q) => q.SupplyTitle ?? q.DemandTitle ?? "交易信息";

    /// <summary>校验报价确实属于该会话, 防止跨会话操作。</summary>
    private async Task EnsureQuoteInConversationAsync(long quoteId, long convId)
    {
        var convOfQuote = await _trade.GetQuoteConversationIdAsync(quoteId);
        if (convOfQuote is null)
            throw new BizException("该报价未关联会话", ErrorCodes.NotFound);
        if (convOfQuote.Value != convId)
            throw new BizException("报价不属于当前会话", ErrorCodes.Forbidden);
    }

    private Task<ChatMessageDto> PushQuoteCardAsync(long convId, long senderId, QuoteDto quote, string title, string summary)
    {
        var payload = new
        {
            kind = "QUOTE", quoteId = quote.Id, quoteNo = quote.QuoteNo,
            supplyId = quote.SupplyId, demandId = quote.DemandId, title,
            price = quote.Price, priceUnit = quote.PriceUnit, quantity = quote.Quantity,
            status = quote.Status, initiatorId = quote.InitiatorId, buyerId = quote.BuyerId, sellerId = quote.SellerId,
        };
        return _chat.SendBusinessAsync(senderId, convId, ChatMessageType.Quote,
            $"{summary}: {quote.Price} {quote.PriceUnit} x {quote.Quantity}", payload, "QUOTE", quote.Id);
    }

    /// <summary>从聊天直接下单: 生成已接受报价→创建订单→向会话推送 ORDER_CARD。</summary>
    public async Task<OrderDto> CreateOrderAsync(long userId, long convId, ChatOrderRequest req)
    {
        await _chat.EnsurePeerReachableAsync(userId, convId);
        var order = await _trade.CreateDirectOrderAsync(userId,
            req.SupplyId, req.DemandId, req.Price, req.PriceUnit, req.Quantity,
            req.DeliveryMode, req.AddressSnapshot, req.Remark, convId);

        var payload = new
        {
            kind = "ORDER", orderId = order.Id, orderNo = order.OrderNo, status = order.Status,
            totalAmount = order.TotalAmount, buyerId = order.BuyerId, sellerId = order.SellerId,
        };
        await _chat.SendBusinessAsync(userId, convId, ChatMessageType.OrderCard,
            $"订单 {order.OrderNo}", payload, "ORDER", order.Id);
        return order;
    }
}
