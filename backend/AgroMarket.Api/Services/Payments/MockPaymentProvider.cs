using AgroMarket.Api.Common;

namespace AgroMarket.Api.Services.Payments;

/// <summary>
/// 开发/演示用支付渠道。仅在 Development 且 Payment:MockEnabled=true 时可用。
/// 立即返回成功并写入 channel=MOCK 的支付记录; 该记录被后台统计明确排除, 不计入真实成交额。
/// </summary>
public class MockPaymentProvider : IPaymentProvider
{
    public string Channel => "MOCK";

    public bool IsAvailable { get; }

    public MockPaymentProvider(IConfiguration config, IWebHostEnvironment env)
        => IsAvailable = env.IsDevelopment() && config.GetValue("Payment:MockEnabled", true);

    public Task<PaymentInitResult> CreateAsync(long orderId, string orderNo, decimal amount, CancellationToken ct = default)
    {
        if (!IsAvailable)
            throw new BizException("MOCK 支付渠道仅在开发环境可用", ErrorCodes.NotImplemented);
        var tradeNo = "MOCK" + orderId.ToString() + "-" + Guid.NewGuid().ToString("N")[..12];
        return Task.FromResult(new PaymentInitResult(true, tradeNo, null));
    }

    /// <summary>Mock 渠道不支持异步回调。</summary>
    public Task<PaymentNotifyResult?> ParseNotifyAsync(PaymentNotifyContext ctx, CancellationToken ct = default)
        => Task.FromResult<PaymentNotifyResult?>(null);
}