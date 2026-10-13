using AgroMarket.Api.Common;
using AgroMarket.Api.Federation.Application;
using AgroMarket.Api.Federation.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Federation.Api;

/// <summary>节点间接口 (对端调用)。全部要求节点令牌, 与用户登录鉴权无关。</summary>
[ApiController]
[Route("api/federation/node")]
[RequireNode]
public class FederationNodeController : ControllerBase
{
    private readonly FederationService _fed;

    public FederationNodeController(FederationService fed) => _fed = fed;

    /// <summary>连通性/身份探测。</summary>
    [HttpGet("ping")]
    public ActionResult<ApiResponse<object>> Ping()
    {
        var me = HttpContext.Node();
        return Ok(ApiResponse<object>.Ok(new { peer = me.NodeCode, peerName = me.NodeName, level = me.Level }));
    }

    /// <summary>接收对端上报。</summary>
    [HttpPost("reports")]
    public async Task<ActionResult<ApiResponse<object>>> Receive([FromBody] FedReportPayload payload)
    {
        var me = HttpContext.Node();
        // 发送方身份必须与令牌一致 (转发数据由逐行的 sourceNodeCode 表达原始来源, 见 NormalizeSource)
        if (!string.Equals(payload.NodeCode, me.NodeCode, StringComparison.OrdinalIgnoreCase))
            throw new BizException("上报节点与令牌身份不一致", ErrorCodes.Forbidden);

        var raw = System.Text.Json.JsonSerializer.Serialize(payload);
        var r = await _fed.IngestAsync(me.NodeCode, payload.BatchNo, payload.PeriodDate, raw, payload.Metrics, payload.Records);
        return Ok(ApiResponse<object>.Ok(new { accepted = true, metricCount = r.MetricCount, recordCount = r.RecordCount }));
    }

    /// <summary>增量导出本节点数据 (供对端拉取)。</summary>
    [HttpGet("export")]
    public async Task<ActionResult<ApiResponse<FedExportDto>>> Export([FromQuery] string? since, [FromQuery] int? limit)
    {
        var sinceTime = DateTime.TryParse(since, out var t) ? t : DateTime.Now.AddDays(-1);
        return Ok(ApiResponse<FedExportDto>.Ok(await _fed.ExportAsync(sinceTime, limit ?? 0)));
    }
}
