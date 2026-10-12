using AgroMarket.Api.Common;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Events;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;

namespace AgroMarket.Api.Services.Payments;

/// <summary>
/// 微信支付(APIv3 Native 扫码)渠道, 基于官方推荐 SDK SKIT.FlurlHttpClient.Wechat.TenpayV3 实现。
/// 需配置 Payment:Wechat:AppId / MchId / ApiV3Key / CertSerialNo / PrivateKey 后方可启用。
/// 未配置时 IsAvailable=false, 发起支付返回 501, 不产生任何支付记录。
/// </summary>
public class WechatPayProvider : IPaymentProvider
{
    public string Channel => "WECHAT";

    public bool IsAvailable { get; }

    private readonly WechatTenpayClient? _client;
    private readonly string _appId;
    private readonly string _notifyUrl;

    public WechatPayProvider(IConfiguration config)
    {
        var s = config.GetSection("Payment:Wechat");
        _appId = s["AppId"] ?? "";
        _notifyUrl = s["NotifyUrl"] ?? "";
        var mchId = s["MchId"] ?? "";
        var serial = s["CertSerialNo"] ?? "";
        var apiV3Key = s["ApiV3Key"] ?? "";
        var privateKey = s["PrivateKey"] ?? "";
        IsAvailable = !string.IsNullOrWhiteSpace(_appId) && !string.IsNullOrWhiteSpace(mchId)
            && !string.IsNullOrWhiteSpace(apiV3Key) && !string.IsNullOrWhiteSpace(serial)
            && !string.IsNullOrWhiteSpace(privateKey);
        if (IsAvailable)
        {
            _client = new WechatTenpayClient(new WechatTenpayClientOptions
            {
                MerchantId = mchId,
                MerchantV3Secret = apiV3Key,
                MerchantCertificateSerialNumber = serial,
                MerchantCertificatePrivateKey = privateKey,
            });
        }
    }

    public async Task<PaymentInitResult> CreateAsync(long orderId, string orderNo, decimal amount, CancellationToken ct = default)
    {
        if (!IsAvailable || _client == null)
            throw new BizException("微信支付渠道未配置, 请在 Payment:Wechat 填写商户参数后启用", ErrorCodes.NotImplemented);

        var request = new CreatePayTransactionNativeRequest
        {
            AppId = _appId,
            OutTradeNumber = orderNo,
            Description = $"息县农贸通订单 {orderNo}",
            NotifyUrl = _notifyUrl,
            Amount = new CreatePayTransactionNativeRequest.Types.Amount
            {
                Total = (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero),
                Currency = "CNY",
            },
        };
        var response = await _client.ExecuteCreatePayTransactionNativeAsync(request, ct);
        if (string.IsNullOrEmpty(response.QrcodeUrl))
            throw new BizException($"微信支付下单失败: {response.ErrorCode} {response.ErrorMessage}", ErrorCodes.ServerError);
        return new PaymentInitResult(false, orderNo, response.QrcodeUrl);
    }

    public Task<PaymentNotifyResult?> ParseNotifyAsync(PaymentNotifyContext ctx, CancellationToken ct = default)
    {
        if (!IsAvailable || _client == null)
            throw new BizException("微信支付渠道未配置", ErrorCodes.NotImplemented);

        string? Header(string name) => ctx.Headers.TryGetValue(name, out var v) ? v : null;
        var timestamp = Header("Wechatpay-Timestamp");
        var nonce = Header("Wechatpay-Nonce");
        var signature = Header("Wechatpay-Signature");
        var serial = Header("Wechatpay-Serial");
        if (timestamp == null || nonce == null || signature == null || serial == null)
            throw new BizException("微信回调缺少签名头", ErrorCodes.BadRequest);

        var verify = _client.VerifyEventSignature(timestamp, nonce, ctx.RawBody, signature, serial);
        if (!verify.Result)
            throw new BizException("微信回调验签失败", ErrorCodes.BadRequest);

        var evt = _client.DeserializeEvent(ctx.RawBody);
        var resource = _client.DecryptEventResource<TransactionResource>(evt);
        var ok = string.Equals(resource.TradeState, "SUCCESS", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult<PaymentNotifyResult?>(
            new PaymentNotifyResult(ok, resource.OutTradeNumber, resource.TransactionId, null));
    }
}
