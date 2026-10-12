using AgroMarket.Api.Common;
using AgroMarket.Api.Dtos;
using AgroMarket.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Controllers;

[ApiController]
[Route("api/payments")]
[RequireAuth]
public class PaymentController : ControllerBase
{
    private readonly PaymentService _payments;
    public PaymentController(PaymentService payments) => _payments = payments;

    /// <summary>可用支付渠道(含是否已配置); 未配置的真实渠道无法发起支付。</summary>
    [HttpGet("channels")]
    public ActionResult<ApiResponse<IReadOnlyList<PaymentChannelDto>>> Channels()
        => Ok(ApiResponse<IReadOnlyList<PaymentChannelDto>>.Ok(_payments.ListChannels()));

    /// <summary>买方对 TO_PAY 订单发起支付。渠道不可用返回 501; 成功则订单进入 TO_SHIP。</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> Create([FromBody] PaymentCreateRequest req, CancellationToken ct)
        => Ok(ApiResponse<PaymentDto>.Ok(await _payments.CreateAsync(HttpContext.User().Id, req, ct), "支付已受理"));

    /// <summary>订单支付记录(买卖双方及管理员可见)。</summary>
    [HttpGet("order/{orderId}")]
    public async Task<ActionResult<ApiResponse<List<PaymentDto>>>> ByOrder(long orderId)
        => Ok(ApiResponse<List<PaymentDto>>.Ok(await _payments.ListByOrderAsync(HttpContext.User().Id, orderId)));
}