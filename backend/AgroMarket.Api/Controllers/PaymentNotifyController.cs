using System.Text;
using AgroMarket.Api.Services;
using AgroMarket.Api.Services.Payments;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Controllers;

/// <summary>
/// 支付渠道异步回调入口(匿名)。微信 APIv3 回调为 JSON + 签名头, 支付宝为表单回调。
/// 回调结果需按各渠道约定格式应答, 否则渠道会重试。
/// </summary>
[ApiController]
[Route("api/payments/notify")]
public class PaymentNotifyController : ControllerBase
{
    private readonly PaymentService _payments;
    public PaymentNotifyController(PaymentService payments) => _payments = payments;

    [HttpPost("{channel}")]
    public async Task<IActionResult> Notify(string channel, CancellationToken ct)
    {
        Request.EnableBuffering();
        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
            body = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in Request.Headers) headers[h.Key] = h.Value.ToString();

        var form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Request.HasFormContentType)
            foreach (var kv in Request.Form) form[kv.Key] = kv.Value.ToString();

        bool ok;
        try
        {
            ok = await _payments.HandleNotifyAsync(channel, new PaymentNotifyContext(body, headers, form), ct);
        }
        catch
        {
            ok = false;
        }

        if (channel.Equals("wechat", StringComparison.OrdinalIgnoreCase))
            return ok
                ? Ok(new { code = "SUCCESS", message = "成功" })
                : Ok(new { code = "FAIL", message = "处理失败" });

        return Content(ok ? "success" : "failure", "text/plain");
    }
}
