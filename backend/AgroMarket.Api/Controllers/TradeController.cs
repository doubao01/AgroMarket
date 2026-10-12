using AgroMarket.Api.Common;
using AgroMarket.Api.Dtos;
using AgroMarket.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Controllers;

[ApiController]
[Route("api/quotes")]
public class QuoteController : ControllerBase
{
    private readonly TradeService _trade;
    public QuoteController(TradeService trade) => _trade = trade;

    /// <summary>提交报价: 对供应商品 (买方) 或对采购需求 (卖方)</summary>
    [HttpPost]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<QuoteDto>>> Create([FromBody] QuoteCreateRequest req)
        => Ok(ApiResponse<QuoteDto>.Ok(await _trade.CreateQuoteAsync(HttpContext.User().Id, req), "报价已提交"));

    /// <summary>我的报价列表: scope=ALL/BUY/SELL; 也可按 supplyId/demandId 查某商品的报价记录</summary>
    [HttpGet]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<PagedResult<QuoteDto>>>> Query(
        [FromQuery] string? scope, [FromQuery] long? supplyId, [FromQuery] long? demandId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(ApiResponse<PagedResult<QuoteDto>>.Ok(
            await _trade.QueryQuotesAsync(HttpContext.User().Id, scope, supplyId, demandId, page, pageSize)));

    [HttpGet("{id}")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<QuoteDto>>> Get(long id)
    {
        var quote = await _trade.GetQuoteByIdAsync(id)
            ?? throw new BizException("报价不存在", ErrorCodes.NotFound);
        var uid = HttpContext.User().Id;
        if (uid != quote.BuyerId && uid != quote.SellerId)
            throw new BizException("无权查看该报价", ErrorCodes.Forbidden);
        return Ok(ApiResponse<QuoteDto>.Ok(quote));
    }

    /// <summary>还价 (协商)</summary>
    [HttpPost("{id}/counter")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<QuoteDto>>> Counter(long id, [FromBody] QuoteCounterRequest req)
        => Ok(ApiResponse<QuoteDto>.Ok(await _trade.CounterQuoteAsync(HttpContext.User().Id, id, req), "还价已发送"));

    [HttpPost("{id}/accept")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<QuoteDto>>> Accept(long id)
        => Ok(ApiResponse<QuoteDto>.Ok(await _trade.AcceptQuoteAsync(HttpContext.User().Id, id), "已接受报价"));

    [HttpPost("{id}/reject")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<QuoteDto>>> Reject(long id)
        => Ok(ApiResponse<QuoteDto>.Ok(await _trade.RejectQuoteAsync(HttpContext.User().Id, id), "已拒绝报价"));
}

[ApiController]
[Route("api/orders")]
[RequireAuth]
public class OrderController : ControllerBase
{
    private readonly TradeService _trade;
    public OrderController(TradeService trade) => _trade = trade;

    /// <summary>买方根据已接受报价确认下单 (生成订单)。未经买方确认不会自动生成订单。</summary>
    [HttpPost("from-quote/{quoteId}")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CreateFromQuote(long quoteId, [FromBody] OrderCreateFromQuoteRequest req)
        => Ok(ApiResponse<OrderDto>.Ok(await _trade.CreateOrderFromQuoteAsync(HttpContext.User().Id, quoteId, req), "订单已创建, 等待确认"));

    [HttpGet]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderDto>>>> Query(
        [FromQuery] string? role, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(ApiResponse<PagedResult<OrderDto>>.Ok(
            await _trade.QueryOrdersAsync(HttpContext.User().Id, role, status, page, pageSize)));

    [HttpGet("{id}")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Get(long id)
        => Ok(ApiResponse<OrderDto>.Ok(await _trade.GetOrderByIdAsync(id, HttpContext.User().Id)));

    /// <summary>confirm 由卖方确认; cancel 取消未付款订单; 支付及后续履约尚未启用。</summary>
    [HttpPost("{id}/status/{operation}")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<OrderDto>>> ChangeStatus(long id, string operation, [FromBody] OrderStatusRequest? req)
    {
        var uid = HttpContext.User().Id;
        var (target, reason, note) = operation switch
        {
            "confirm" => ("TO_PAY", (string?)null, "卖方已确认订单, 等待买方付款"),
            "markPaid" => ("TO_SHIP", null, (string?)null),
            "ship" => ("SHIPPING", null, "卖方已发货"),
            "receive" => ("COMPLETED", null, "买方已确认收货"),
            "afterSale" => ("AFTER_SALE", req?.Reason, "买方申请售后"),
            "cancel" => ("CANCELLED", req?.Reason, req?.Note),
            _ => throw new BizException("不支持的操作"),
        };
        var order = await _trade.ChangeOrderStatusAsync(uid, id, target, reason, note);
        return Ok(ApiResponse<OrderDto>.Ok(order, "操作成功"));
    }
}
