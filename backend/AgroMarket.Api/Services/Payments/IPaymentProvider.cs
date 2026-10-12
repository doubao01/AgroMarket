namespace AgroMarket.Api.Services.Payments;

/// <summary>渠道下单结果: Success 表示渠道已即时确认收款(如开发态 Mock); 否则给出待支付凭据。</summary>
public record PaymentInitResult(bool Success, string? TradeNo, string? PayUrl);

/// <summary>渠道异步回调上下文: 原始报文 + 请求头 + 表单字段。</summary>
public record PaymentNotifyContext(
    string RawBody,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Form);

/// <summary>渠道回调解析结果: Success 表示该笔交易已支付成功。</summary>
public record PaymentNotifyResult(bool Success, string OrderNo, string? TradeNo, string? RawPayload);

/// <summary>
/// 可插拔支付渠道。真实渠道(微信/支付宝)基于各自官方 SDK 实现, 需在渠道后台申请资质并配置密钥后方可 IsAvailable;
/// 未配置时 CreateAsync/ParseNotifyAsync 抛出 501, 绝不伪造支付结果。
/// </summary>
public interface IPaymentProvider
{
    /// <summary>渠道标识, 与 payment_record.channel 对应, 例如 MOCK / WECHAT / ALIPAY。</summary>
    string Channel { get; }

    /// <summary>渠道是否已配置且启用。未配置的真实渠道返回 false。</summary>
    bool IsAvailable { get; }

    /// <summary>向渠道发起支付。返回结果决定订单是否可进入 TO_SHIP。</summary>
    Task<PaymentInitResult> CreateAsync(long orderId, string orderNo, decimal amount, CancellationToken ct = default);

    /// <summary>
    /// 解析并校验渠道异步回调。返回 null 表示该渠道不支持回调处理(如 Mock);
    /// 验签失败或报文非法时抛出 400。
    /// </summary>
    Task<PaymentNotifyResult?> ParseNotifyAsync(PaymentNotifyContext ctx, CancellationToken ct = default);
}
