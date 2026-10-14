using AgroMarket.Api.Common;
using AgroMarket.Api.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Controllers;

/// <summary>微信订阅消息授权结果上报。</summary>
[ApiController]
[Route("api/wechat")]
[RequireAuth]
public class WeChatController : ControllerBase
{
    private readonly WeChatPushService _wechat;
    public WeChatController(WeChatPushService wechat) => _wechat = wechat;

    /// <summary>用户在小程序内授权订阅后上报; accepted=false 忽略。</summary>
    [HttpPost("subscribe-report")]
    public async Task<ActionResult<ApiResponse<object>>> SubscribeReport([FromBody] SubscribeReportReq req)
    {
        await _wechat.ReportSubscribeQuotaAsync(HttpContext.User().Id, req.TemplateKey, req.Accepted);
        return Ok(ApiResponse<object>.Ok(null));
    }

    public record SubscribeReportReq(string TemplateKey, bool Accepted);
}
