using AgroMarket.Api.Common;
using AgroMarket.Api.Federation.Application;
using AgroMarket.Api.Federation.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Federation.Api;

/// <summary>联动管理接口 (仅管理员): 节点注册、令牌签发、上报审计、查询与汇聚、手动同步。</summary>
[ApiController]
[Route("api/admin/federation")]
[RequireAdmin]
public class FederationAdminController : ControllerBase
{
    private readonly FederationAdminService _admin;
    private readonly FederationService _fed;

    public FederationAdminController(FederationAdminService admin, FederationService fed)
    {
        _admin = admin;
        _fed = fed;
    }

    [HttpGet("self")]
    public ActionResult<ApiResponse<FedSelfDto>> Self()
        => Ok(ApiResponse<FedSelfDto>.Ok(_admin.GetSelf()));

    [HttpGet("nodes")]
    public async Task<ActionResult<ApiResponse<List<FedNodeDto>>>> Nodes()
        => Ok(ApiResponse<List<FedNodeDto>>.Ok(await _admin.ListNodesAsync()));

    [HttpPost("nodes")]
    public async Task<ActionResult<ApiResponse<object>>> UpsertNode([FromBody] FedNodeUpsertRequest req)
    {
        var id = await _admin.UpsertNodeAsync(req);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPut("nodes/{id:long}")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateNode(long id, [FromBody] FedNodeUpsertRequest req)
    {
        await _admin.UpdateNodeAsync(id, req);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPost("nodes/{id:long}/status")]
    public async Task<ActionResult<ApiResponse<object>>> SetStatus(long id, [FromBody] StatusBody body)
    {
        await _admin.SetNodeStatusAsync(id, body.Status ?? "");
        return Ok(ApiResponse<object>.Ok(new { id, body.Status }));
    }

    [HttpDelete("nodes/{id:long}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteNode(long id)
    {
        await _admin.DeleteNodeAsync(id);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    /// <summary>签发节点令牌 (nodeCode 为空则签发本节点)。</summary>
    [HttpPost("nodes/token")]
    public ActionResult<ApiResponse<object>> IssueToken([FromBody] FedIssueTokenRequest req)
        => Ok(ApiResponse<object>.Ok(_admin.IssueToken(req)));

    [HttpGet("reports")]
    public async Task<ActionResult<ApiResponse<object>>> Reports([FromQuery] string? source, [FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        var (items, total) = await _admin.ListReportsAsync(source, page, size);
        return Ok(ApiResponse<object>.Ok(new { items, total, page, size }));
    }

    [HttpGet("metrics")]
    public async Task<ActionResult<ApiResponse<List<FedMetricDto>>>> Metrics(
        [FromQuery] string? source, [FromQuery] string? range,
        [FromQuery] string? from = null, [FromQuery] string? to = null)
        => Ok(ApiResponse<List<FedMetricDto>>.Ok(await _admin.ListMetricsAsync(source, range, from, to)));

    [HttpGet("records")]
    public async Task<ActionResult<ApiResponse<List<FedRecordViewDto>>>> Records(
        [FromQuery] string? source, [FromQuery] string? type,
        [FromQuery] int days = 7, [FromQuery] int limit = 50, [FromQuery] int offset = 0)
        => Ok(ApiResponse<List<FedRecordViewDto>>.Ok(await _admin.ListRecordsAsync(source, type, days, limit, offset)));

    [HttpGet("aggregate")]
    public async Task<ActionResult<ApiResponse<FedAggregateDto>>> Aggregate([FromQuery] string? range)
        => Ok(ApiResponse<FedAggregateDto>.Ok(await _admin.AggregateAsync(range)));

    /// <summary>手动推送到对端 (node 为空则推送到全部 PUSH/BOTH 节点)。</summary>
    [HttpPost("sync/push")]
    public async Task<ActionResult<ApiResponse<FedSyncResultDto>>> SyncPush([FromQuery] string? node)
        => Ok(ApiResponse<FedSyncResultDto>.Ok(await _fed.PushAsync(node)));

    /// <summary>手动从对端拉取 (node 为空则拉取全部 PULL/BOTH 节点)。</summary>
    [HttpPost("sync/pull")]
    public async Task<ActionResult<ApiResponse<FedSyncResultDto>>> SyncPull([FromQuery] string? node)
        => Ok(ApiResponse<FedSyncResultDto>.Ok(await _fed.PullAsync(node)));

    public record StatusBody(string? Status);
}
