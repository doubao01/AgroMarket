using AgroMarket.Api.Common;
using AgroMarket.Api.Dtos;
using AgroMarket.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Controllers;

/// <summary>数据大屏统计接口 (仅管理员)。</summary>
[ApiController]
[Route("api/admin/dashboard")]
[RequireAdmin]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _dash;
    public DashboardController(DashboardService dash) => _dash = dash;

    private long Uid => HttpContext.User().Id;

    [HttpGet("overview")]
    public async Task<ActionResult<ApiResponse<DashboardOverviewDto>>> Overview()
        => Ok(ApiResponse<DashboardOverviewDto>.Ok(await _dash.GetOverviewAsync(Uid)));

    [HttpGet("trend")]
    public async Task<ActionResult<ApiResponse<List<TrendPointDto>>>> Trend([FromQuery] string? range)
        => Ok(ApiResponse<List<TrendPointDto>>.Ok(await _dash.GetTrendAsync(Uid, range)));

    [HttpGet("category")]
    public async Task<ActionResult<ApiResponse<List<CategoryStatDto>>>> Category()
        => Ok(ApiResponse<List<CategoryStatDto>>.Ok(await _dash.GetCategoryAsync(Uid)));

    [HttpGet("region")]
    public async Task<ActionResult<ApiResponse<List<RegionStatDto>>>> Region()
        => Ok(ApiResponse<List<RegionStatDto>>.Ok(await _dash.GetRegionAsync(Uid)));

    [HttpGet("users")]
    public async Task<ActionResult<ApiResponse<List<RoleStatDto>>>> Users()
        => Ok(ApiResponse<List<RoleStatDto>>.Ok(await _dash.GetUsersAsync(Uid)));

    [HttpGet("users/trend")]
    public async Task<ActionResult<ApiResponse<List<UserTrendPointDto>>>> UsersTrend([FromQuery] string? range)
        => Ok(ApiResponse<List<UserTrendPointDto>>.Ok(await _dash.GetUsersTrendAsync(Uid, range)));

    [HttpGet("chat")]
    public async Task<ActionResult<ApiResponse<List<ChatActivityDto>>>> Chat([FromQuery] string? range)
        => Ok(ApiResponse<List<ChatActivityDto>>.Ok(await _dash.GetChatActivityAsync(Uid, range)));
}
