using AgroMarket.Api.Common;
using Dapper;
using AgroMarket.Api.Dtos;
using AgroMarket.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Controllers;

[ApiController]
[Route("api/supplies")]
public class SupplyController : ControllerBase
{
    private readonly SupplyService _supplies;
    public SupplyController(SupplyService supplies) => _supplies = supplies;

    /// <summary>供应列表。status=PUBLIC(默认,审核通过) / MINE(我的,需登录) / 具体状态。townId + townIncludeVillages=true 时含下属村。</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<SupplyDto>>>> Query([FromQuery] SupplyQuery q)
    {
        return Ok(ApiResponse<PagedResult<SupplyDto>>.Ok(await _supplies.QueryAsync(q, HttpContext.User().Id)));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<SupplyDto>>> Get(long id)
    {
        var uid = HttpContext.User().Id;
        return Ok(ApiResponse<SupplyDto>.Ok(await _supplies.GetByIdAsync(id, uid == 0 ? null : uid)));
    }

    [HttpPost]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<IdRequest>>> Create([FromBody] SupplyCreateRequest req)
    {
        var id = await _supplies.CreateAsync(HttpContext.User().Id, req);
        return Ok(ApiResponse<IdRequest>.Ok(new IdRequest(id), "发布成功, 等待审核"));
    }

    [HttpPut("{id}")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> Update(long id, [FromBody] SupplyCreateRequest req)
    {
        await _supplies.UpdateAsync(HttpContext.User().Id, id, req);
        return Ok(ApiResponse<object>.Ok(null, "修改成功, 等待重新审核"));
    }

    /// <summary>状态操作: off_shelf 下架 / re_activate 上架 / sold_out 售罄</summary>
    [HttpPost("{id}/status/{operation}")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> ChangeStatus(long id, string operation)
    {
        await _supplies.ChangeStatusAsync(HttpContext.User().Id, id, operation);
        return Ok(ApiResponse<object>.Ok(null, "操作成功"));
    }
}

[ApiController]
[Route("api/demands")]
public class DemandController : ControllerBase
{
    private readonly DemandService _demands;
    public DemandController(DemandService demands) => _demands = demands;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<DemandDto>>>> Query([FromQuery] DemandQuery q)
    {
        return Ok(ApiResponse<PagedResult<DemandDto>>.Ok(await _demands.QueryAsync(q, HttpContext.User().Id)));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<DemandDto>>> Get(long id)
    {
        var uid = HttpContext.User().Id;
        return Ok(ApiResponse<DemandDto>.Ok(await _demands.GetByIdAsync(id, uid == 0 ? null : uid)));
    }

    [HttpPost]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<IdRequest>>> Create([FromBody] DemandCreateRequest req)
    {
        var id = await _demands.CreateAsync(HttpContext.User().Id, req);
        return Ok(ApiResponse<IdRequest>.Ok(new IdRequest(id), "发布成功, 等待审核"));
    }

    [HttpPut("{id}")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> Update(long id, [FromBody] DemandCreateRequest req)
    {
        await _demands.UpdateAsync(HttpContext.User().Id, id, req);
        return Ok(ApiResponse<object>.Ok(null, "修改成功, 等待重新审核"));
    }

    [HttpPost("{id}/status/{operation}")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> ChangeStatus(long id, string operation)
    {
        await _demands.ChangeStatusAsync(HttpContext.User().Id, id, operation);
        return Ok(ApiResponse<object>.Ok(null, "操作成功"));
    }
}

[ApiController]
[Route("api/categories")]
public class CategoryController : ControllerBase
{
    private readonly IDbConnectionFactory _db;
    public CategoryController(IDbConnectionFactory db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<object>>> All()
    {
        using var conn = _db.Create();
        var items = (await conn.QueryAsync<object>(
            "SELECT id, name, parent_id ParentId, sort_order SortOrder FROM product_category WHERE is_enabled=1 ORDER BY parent_id IS NOT NULL, sort_order, id")).ToList();
        return Ok(ApiResponse<object>.Ok(items));
    }
}
