using AgroMarket.Api.Common;
using AgroMarket.Api.Dtos;
using AgroMarket.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Controllers;

[ApiController]
[Route("api/regions")]
public class RegionController : ControllerBase
{
    private readonly RegionService _regions;
    public RegionController(RegionService regions) => _regions = regions;

    /// <summary>省列表 (无参数时即省级联)</summary>
    [HttpGet("provinces")]
    public async Task<ActionResult<ApiResponse<List<RegionDto>>>> Provinces()
        => Ok(ApiResponse<List<RegionDto>>.Ok(await _regions.GetChildrenAsync(null)));

    [HttpGet("cities")]
    public async Task<ActionResult<ApiResponse<List<RegionDto>>>> Cities([FromQuery] long parentId)
        => Ok(ApiResponse<List<RegionDto>>.Ok(await _regions.GetChildrenAsync(parentId)));

    [HttpGet("counties")]
    public async Task<ActionResult<ApiResponse<List<RegionDto>>>> Counties([FromQuery] long parentId)
        => Ok(ApiResponse<List<RegionDto>>.Ok(await _regions.GetChildrenAsync(parentId)));

    [HttpGet("towns")]
    public async Task<ActionResult<ApiResponse<List<RegionDto>>>> Towns([FromQuery] long parentId)
        => Ok(ApiResponse<List<RegionDto>>.Ok(await _regions.GetChildrenAsync(parentId)));

    [HttpGet("villages")]
    public async Task<ActionResult<ApiResponse<List<RegionDto>>>> Villages([FromQuery] long parentId)
        => Ok(ApiResponse<List<RegionDto>>.Ok(await _regions.GetChildrenAsync(parentId)));

    /// <summary>统一子区域接口: children?parentId=xx</summary>
    [HttpGet("children")]
    public async Task<ActionResult<ApiResponse<List<RegionDto>>>> Children([FromQuery] long? parentId)
        => Ok(ApiResponse<List<RegionDto>>.Ok(await _regions.GetChildrenAsync(parentId)));

    /// <summary>区域树: tree?rootCode=411528 返回息县子树</summary>
    [HttpGet("tree")]
    public async Task<ActionResult<ApiResponse<List<RegionNode>>>> Tree([FromQuery] string? rootCode)
        => Ok(ApiResponse<List<RegionNode>>.Ok(await _regions.GetTreeAsync(rootCode)));
}
