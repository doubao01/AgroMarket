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

    /// <summary>市级列表: 传入的 parentId 必须是省级 (region_level=1)</summary>
    [HttpGet("cities")]
    public async Task<ActionResult<ApiResponse<List<RegionDto>>>> Cities([FromQuery] long parentId)
    {
        await _regions.ValidateRegionAsync(parentId, null, 1);
        return Ok(ApiResponse<List<RegionDto>>.Ok(await _regions.GetChildrenAsync(parentId)));
    }

    /// <summary>区县列表: 传入的 parentId 必须是市级 (region_level=2)</summary>
    [HttpGet("counties")]
    public async Task<ActionResult<ApiResponse<List<RegionDto>>>> Counties([FromQuery] long parentId)
    {
        await _regions.ValidateRegionAsync(parentId, null, 2);
        return Ok(ApiResponse<List<RegionDto>>.Ok(await _regions.GetChildrenAsync(parentId)));
    }

    /// <summary>乡镇列表: 传入的 parentId 必须是区县级 (region_level=3)</summary>
    [HttpGet("towns")]
    public async Task<ActionResult<ApiResponse<List<RegionDto>>>> Towns([FromQuery] long parentId)
    {
        await _regions.ValidateRegionAsync(parentId, null, 3);
        return Ok(ApiResponse<List<RegionDto>>.Ok(await _regions.GetChildrenAsync(parentId)));
    }

    /// <summary>村级列表: 传入的 parentId 必须是乡镇级 (region_level=4)</summary>
    [HttpGet("villages")]
    public async Task<ActionResult<ApiResponse<List<RegionDto>>>> Villages([FromQuery] long parentId)
    {
        await _regions.ValidateRegionAsync(parentId, null, 4);
        return Ok(ApiResponse<List<RegionDto>>.Ok(await _regions.GetChildrenAsync(parentId)));
    }

    /// <summary>统一子区域接口: children?parentId=xx</summary>
    [HttpGet("children")]
    public async Task<ActionResult<ApiResponse<List<RegionDto>>>> Children([FromQuery] long? parentId)
        => Ok(ApiResponse<List<RegionDto>>.Ok(await _regions.GetChildrenAsync(parentId)));

    /// <summary>区域树: tree?rootCode=411528 返回息县子树</summary>
    [HttpGet("tree")]
    public async Task<ActionResult<ApiResponse<List<RegionNode>>>> Tree([FromQuery] string? rootCode)
        => Ok(ApiResponse<List<RegionNode>>.Ok(await _regions.GetTreeAsync(rootCode)));
}
