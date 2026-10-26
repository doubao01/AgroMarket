using AgroMarket.Api.Common;
using Dapper;
using AgroMarket.Api.Dtos;
using AgroMarket.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Controllers;

[ApiController]
[Route("api/admin")]
[RequireAdmin]
public class AdminController : ControllerBase
{
    private readonly AdminService _admin;
    public AdminController(AdminService admin) => _admin = admin;

    private long Uid => HttpContext.User().Id;

    // ---------- 用户 ----------
    [HttpGet("users")]
    public async Task<ActionResult<ApiResponse<PagedResult<object>>>> Users(
        [FromQuery] string? keyword, [FromQuery] string? role, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(ApiResponse<PagedResult<object>>.Ok(await _admin.QueryUsersAsync(Uid, keyword, role, status, page, pageSize, HttpContext.ClientIp())));

    [HttpPost("users/{id}/freeze/{freeze}")]
    public async Task<ActionResult<ApiResponse<object>>> Freeze(long id, bool freeze)
    {
        await _admin.FreezeUserAsync(Uid, id, freeze, HttpContext.ClientIp());
        return Ok(ApiResponse<object>.Ok(null, freeze ? "已冻结" : "已解冻"));
    }

    // ---------- 认证审核 ----------
    [HttpGet("auths")]
    public async Task<ActionResult<ApiResponse<PagedResult<object>>>> Auths(
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(ApiResponse<PagedResult<object>>.Ok(await _admin.QueryAuthsAsync(Uid, status, page, pageSize, HttpContext.ClientIp())));

    /// <summary>查看完整身份证号(列表只给脱敏值)。会写审计日志。</summary>
    [HttpGet("auths/{id:long}/id-number")]
    public async Task<ActionResult<ApiResponse<object>>> AuthIdNumber(long id)
        => Ok(ApiResponse<object>.Ok(new { idNumber = await _admin.GetAuthIdNumberAsync(Uid, id, HttpContext.ClientIp()) }));

    [HttpPost("auths/{id}/audit/{approve}")]
    public async Task<ActionResult<ApiResponse<object>>> AuditAuth(long id, bool approve, [FromBody] RemarkRequest? req)
    {
        await _admin.AuditAuthAsync(Uid, id, approve, req?.Remark);
        return Ok(ApiResponse<object>.Ok(null, approve ? "已通过" : "已驳回"));
    }

    // ---------- 供应/采购审核 ----------
    [HttpGet("supplies")]
    public async Task<ActionResult<ApiResponse<PagedResult<SupplyDto>>>> Supplies([FromQuery] SupplyQuery q)
        => Ok(ApiResponse<PagedResult<SupplyDto>>.Ok(await _suppliesQuery(q)));

    private Task<PagedResult<SupplyDto>> _suppliesQuery(SupplyQuery q)
    {
        var svc = HttpContext.RequestServices.GetRequiredService<SupplyService>();
        return svc.QueryAsync(q, Uid, admin: true);
    }

    [HttpPost("supplies/{id}/audit/{approve}")]
    public async Task<ActionResult<ApiResponse<object>>> AuditSupply(long id, bool approve, [FromBody] RemarkRequest? req)
    {
        await _admin.AuditSupplyAsync(Uid, id, approve, req?.Remark);
        return Ok(ApiResponse<object>.Ok(null, approve ? "已上架" : "已驳回下架"));
    }

    [HttpGet("demands")]
    public async Task<ActionResult<ApiResponse<PagedResult<DemandDto>>>> Demands([FromQuery] DemandQuery q)
    {
        var svc = HttpContext.RequestServices.GetRequiredService<DemandService>();
        return Ok(ApiResponse<PagedResult<DemandDto>>.Ok(await svc.QueryAsync(q, Uid, admin: true)));
    }

    [HttpPost("demands/{id}/audit/{approve}")]
    public async Task<ActionResult<ApiResponse<object>>> AuditDemand(long id, bool approve, [FromBody] RemarkRequest? req)
    {
        await _admin.AuditDemandAsync(Uid, id, approve, req?.Remark);
        return Ok(ApiResponse<object>.Ok(null, approve ? "已上架" : "已驳回"));
    }

    // ---------- 区域管理 ----------
    [HttpPost("regions")]
    public async Task<ActionResult<ApiResponse<object>>> CreateRegion([FromBody] RegionCreateRequest req)
        => Ok(ApiResponse<object>.Ok(await _admin.CreateRegionAsync(Uid, req.Code, req.Name, req.ParentId, req.Level), "区域已创建"));

    [HttpPost("regions/{id}/enabled/{enabled}")]
    public async Task<ActionResult<ApiResponse<object>>> SetRegionEnabled(long id, bool enabled)
    {
        await _admin.SetRegionEnabledAsync(Uid, id, enabled);
        return Ok(ApiResponse<object>.Ok(null, enabled ? "已启用" : "已停用"));
    }

    [HttpPost("regions/{id}/rename")]
    public async Task<ActionResult<ApiResponse<object>>> RenameRegion(long id, [FromBody] RegionRenameRequest req)
    {
        await _admin.RenameRegionAsync(Uid, id, req.Name);
        return Ok(ApiResponse<object>.Ok(null, "已改名"));
    }

    // ---------- 举报 ----------
    [HttpGet("reports")]
    public async Task<ActionResult<ApiResponse<PagedResult<ReportDto>>>> Reports(
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(ApiResponse<PagedResult<ReportDto>>.Ok(await _admin.QueryReportsAsync(Uid, status, page, pageSize)));

    [HttpPost("reports/{id}/handle/{resolve}")]
    public async Task<ActionResult<ApiResponse<object>>> HandleReport(long id, bool resolve, [FromBody] RemarkRequest? req)
    {
        await _admin.HandleReportAsync(Uid, id, resolve, req?.Remark);
        return Ok(ApiResponse<object>.Ok(null, "已处理"));
    }

    // ---------- 公告 ----------
    [HttpPost("notices")]
    public async Task<ActionResult<ApiResponse<IdRequest>>> CreateNotice([FromBody] NoticeRequest req)
        => Ok(ApiResponse<IdRequest>.Ok(new IdRequest(await _admin.CreateNoticeAsync(Uid, req.Title, req.Content)), "公告已发布"));

    [HttpPost("notices/{id}/unpublish")]
    public async Task<ActionResult<ApiResponse<object>>> UnpublishNotice(long id)
    {
        await _admin.DeleteNoticeAsync(Uid, id);
        return Ok(ApiResponse<object>.Ok(null, "公告已下线"));
    }

    // ---------- 统计 ----------
    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<StatsDto>>> Stats()
        => Ok(ApiResponse<StatsDto>.Ok(await _admin.GetStatsAsync(Uid)));

    // ---------- 操作日志 ----------
    [HttpGet("logs")]
    public async Task<ActionResult<ApiResponse<PagedResult<object>>>> Logs(
        [FromQuery] string? module, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        using var conn = HttpContext.RequestServices.GetRequiredService<IDbConnectionFactory>().Create();
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;
        var p = new Dapper.DynamicParameters();
        var where = "";
        if (!string.IsNullOrWhiteSpace(module)) { where = "WHERE module=@m"; p.Add("m", module); }
        p.Add("limit", pageSize); p.Add("offset", Paging.Offset(page, pageSize));
        var total = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM sys_operation_log {where}", p);
        var items = (await conn.QueryAsync<object>(
            $@"SELECT l.id, l.operator_id OperatorId, u.nickname OperatorName, l.module Module, l.action Action,
                      l.target_id TargetId, l.detail Detail, l.ip, l.created_at CreatedAt
               FROM sys_operation_log l LEFT JOIN sys_user u ON u.id = l.operator_id {where}
               ORDER BY l.id DESC LIMIT @limit OFFSET @offset", p)).ToList();
        return Ok(ApiResponse<PagedResult<object>>.Ok(new PagedResult<object>(items, total, page, pageSize)));
    }

    /// <summary>
    /// 手动触发一轮订单超时关闭(与后台任务同一套逻辑, 有数据库锁互斥)。
    /// 运维应急用: 后台任务异常或积压时可立即回收库存。
    /// </summary>
    [HttpPost("orders/close-expired")]
    public async Task<ActionResult<ApiResponse<object>>> CloseExpiredOrders()
    {
        var worker = HttpContext.RequestServices.GetRequiredService<AgroMarket.Api.Notifications.OrderTimeoutWorker>();
        var (closed, busy) = await worker.RunOnceAsync();
        // 命名锁被占用说明后台循环或另一实例正在跑: 不能回"已关闭 0 个"冒充成功,
        // 运维会误判为"没有积压"。
        if (busy)
            return Conflict(ApiResponse<object>.Fail(ErrorCodes.Conflict, "超时任务正在进行中, 请稍后重试"));
        return Ok(ApiResponse<object>.Ok(new { closed }, $"已关闭 {closed} 个超时订单"));
    }

    public record RemarkRequest(string? Remark);
    public record RegionCreateRequest(string Code, string Name, long? ParentId, int Level);
    public record RegionRenameRequest(string Name);
    public record NoticeRequest(string Title, string Content);
}
