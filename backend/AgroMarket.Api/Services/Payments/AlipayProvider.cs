using AgroMarket.Api.Common;
using Aop.Api;
using Aop.Api.Domain;
using Aop.Api.Request;
using Aop.Api.Util;

namespace AgroMarket.Api.Services.Payments;

/// <summary>
/// 支付宝(当面付/扫码预下单)渠道, 基于蚂蚁官方 SDK AlipaySDKNet.Standard 实现。
/// 需配置 Payment:Alipay:AppId / PrivateKey / AlipayPublicKey 后方可启用。
/// 未配置时 IsAvailable=false, 发起支付返回 501, 不产生任何支付记录。
/// </summary>
public class AlipayProvider : IPaymentProvider
{
    public string Channel => "ALIPAY";

    public bool IsAvailable { get; }

    private readonly AlipayConfig? _config;
    private readonly string _notifyUrl;

    public AlipayProvider(IConfiguration config)
    {
        var s = config.GetSection("Payment:Alipay");
        var appId = s["AppId"] ?? "";
        var privateKey = s["PrivateKey"] ?? "";
        var alipayPublicKey = s["AlipayPublicKey"] ?? "";
        _notifyUrl = s["NotifyUrl"] ?? "";
        IsAvailable = !string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(privateKey)
            && !string.IsNullOrWhiteSpace(alipayPublicKey);
        if (IsAvailable)
        {
            _config = new AlipayConfig
            {
                ServerUrl = string.IsNullOrWhiteSpace(s["Gateway"]) ? "https://openapi.alipay.com/gateway.do" : s["Gateway"]!,
                AppId = appId,
                PrivateKey = privateKey,
                AlipayPublicKey = alipayPublicKey,
                Format = "json",
                Charset = "UTF-8",
                SignType = "RSA2",
            };
        }
    }

    public Task<PaymentInitResult> CreateAsync(long orderId, string orderNo, decimal amount, CancellationToken ct = default)
    {
        if (!IsAvailable || _config == null)
            throw new BizException("支付宝渠道未配置, 请在 Payment:Alipay 填写应用参数后启用", ErrorCodes.NotImplemented);

        var client = new DefaultAopClient(_config);
        var request = new AlipayTradePrecreateRequest();
        if (!string.IsNullOrWhiteSpace(_notifyUrl)) request.SetNotifyUrl(_notifyUrl);
        request.SetBizModel(new AlipayTradePrecreateModel
        {
            OutTradeNo = orderNo,
            TotalAmount = amount.ToString("0.00"),
            Subject = $"息县农贸通订单 {orderNo}",
        });
        var response = client.Execute(request);
        if (response == null || string.IsNullOrEmpty(response.QrCode))
            throw new BizException($"支付宝下单失败: {response?.SubCode} {response?.SubMsg}", ErrorCodes.ServerError);
        return Task.FromResult(new PaymentInitResult(false, orderNo, response.QrCode));
    }

    public Task<PaymentNotifyResult?> ParseNotifyAsync(PaymentNotifyContext ctx, CancellationToken ct = default)
    {
        if (!IsAvailable || _config == null)
            throw new BizException("支付宝渠道未配置", ErrorCodes.NotImplemented);

        var form = new Dictionary<string, string>(ctx.Form, StringComparer.OrdinalIgnoreCase);
        var signType = form.TryGetValue("sign_type", out var st) && !string.IsNullOrWhiteSpace(st) ? st : "RSA2";
        var verified = AlipaySignature.RSACheckV1(form, _config.AlipayPublicKey, "UTF-8", signType, false);
        if (!verified)
            throw new BizException("支付宝回调验签失败", ErrorCodes.BadRequest);

        form.TryGetValue("out_trade_no", out var orderNo);
        form.TryGetValue("trade_no", out var tradeNo);
        form.TryGetValue("trade_status", out var status);
        var ok = status is "TRADE_SUCCESS" or "TRADE_FINISHED";
        return Task.FromResult<PaymentNotifyResult?>(new PaymentNotifyResult(ok, orderNo ?? "", tradeNo, null));
    }
}
