using AgroMarket.Api.Common;
using AgroMarket.Api.Dtos;
using AgroMarket.Api.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Controllers;

/// <summary>管理端报警中心: 规则查询/修改、报警记录、手动触发扫描 (仅管理员)。</summary>
[ApiController]
[Route("api/admin/alerts")]
[RequireAdmin]
public class AlertController : ControllerBase
{
    private readonly AlertService _alerts;
    public AlertController(AlertService alerts) => _alerts = alerts;

    [HttpGet("rules")]
    public async Task<ActionResult<ApiResponse<List<AlertService.AlertRule>>>> Rules()
        => Ok(ApiResponse<List<AlertService.AlertRule>>.Ok(await _alerts.GetRulesAsync()));

    [HttpPut("rules/{code}")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateRule(string code, [FromBody] UpdateRuleReq req)
    {
        var operatorId = HttpContext.User().Id;
        var thresholdJson = req.ThresholdValue == null
            ? null
            : System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, int> { [ThresholdKeyOf(code)] = req.ThresholdValue.Value });
        await _alerts.UpdateRuleAsync(operatorId, code, req.Enabled, thresholdJson, req.SuppressHours,
            req.RecipientIds == null ? null : string.Join(",", req.RecipientIds));
        return Ok(ApiResponse<object>.Ok(null));
    }

    [HttpGet("records")]
    public async Task<ActionResult<ApiResponse<Dtos.PagedResult<AlertService.AlertRecordDto>>>> Records(
        [FromQuery] string? ruleCode, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(ApiResponse<Dtos.PagedResult<AlertService.AlertRecordDto>>.Ok(
            await _alerts.QueryRecordsAsync(Math.Max(1, page), Math.Clamp(pageSize, 1, 50), ruleCode)));

    [HttpPost("scan")]
    public async Task<ActionResult<ApiResponse<AlertService.ScanSummary>>> Scan()
        => Ok(ApiResponse<AlertService.ScanSummary>.Ok(await _alerts.ScanAsync()));

    public record UpdateRuleReq(bool? Enabled, int? ThresholdValue, List<long>? RecipientIds, int? SuppressHours);

    private static string ThresholdKeyOf(string code) => code switch
    {
        "ORDER_PAY_TIMEOUT" or "QUOTE_NO_RESPONSE" => "hours",
        "AUDIT_BACKLOG" => "count",
        "TRADE_ANOMALY" => "percent",
        _ => "value",
    };
}
