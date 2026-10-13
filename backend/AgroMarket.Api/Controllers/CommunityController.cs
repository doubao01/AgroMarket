using AgroMarket.Api.Common;
using AgroMarket.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Controllers;

/// <summary>焦点图 (首页轮播, 公开)。</summary>
[ApiController]
[Route("api/banners")]
public class BannerController : ControllerBase
{
    private readonly CommunityService _community;
    public BannerController(CommunityService community) => _community = community;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<CommunityService.BannerDto>>>> List()
        => Ok(ApiResponse<List<CommunityService.BannerDto>>.Ok(await _community.ListBannersAsync()));
}

/// <summary>社区: 发帖 / 浏览 / 评论 / 点赞。</summary>
[ApiController]
[Route("api/community")]
public class CommunityController : ControllerBase
{
    private readonly CommunityService _community;
    public CommunityController(CommunityService community) => _community = community;

    private long? Uid
    {
        get
        {
            try { return HttpContext.User().Id; } catch { return null; }
        }
    }

    [HttpGet("posts")]
    public async Task<ActionResult<ApiResponse<Dtos.PagedResult<CommunityService.PostDto>>>> Posts(
        [FromQuery] string? topic, [FromQuery] string orderBy = "new", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(ApiResponse<Dtos.PagedResult<CommunityService.PostDto>>.Ok(
            await _community.ListPostsAsync(topic, orderBy, page, Math.Clamp(pageSize, 1, 50))));

    [HttpGet("posts/{id}")]
    public async Task<ActionResult<ApiResponse<object>>> Post(long id)
        => Ok(ApiResponse<object>.Ok(await _community.GetPostAsync(id, Uid)));

    [HttpPost("posts")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> CreatePost([FromBody] CommunityService.PostUpsert req)
        => Ok(ApiResponse<object>.Ok(await _community.CreatePostAsync(HttpContext.User().Id, req), "已发布"));

    [HttpDelete("posts/{id}")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> DeletePost(long id)
    {
        await _community.DeletePostAsync(HttpContext.User().Id, id);
        return Ok(ApiResponse<object>.Ok(null));
    }

    [HttpPost("posts/{id}/comments")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> AddComment(long id, [FromBody] CommunityService.CommentUpsert req)
        => Ok(ApiResponse<object>.Ok(await _community.AddCommentAsync(HttpContext.User().Id, id, req), "评论成功"));

    [HttpPost("posts/{id}/like")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> ToggleLike(long id)
        => Ok(ApiResponse<object>.Ok(new { liked = await _community.ToggleLikeAsync(HttpContext.User().Id, id) }));
}

/// <summary>管理端: 焦点图维护 + 社区帖子治理 (仅管理员)。</summary>
[ApiController]
[Route("api/admin/community")]
[RequireAdmin]
public class CommunityAdminController : ControllerBase
{
    private readonly CommunityService _community;
    public CommunityAdminController(CommunityService community) => _community = community;

    [HttpPost("banners")]
    public async Task<ActionResult<ApiResponse<object>>> SaveBanner([FromBody] CommunityService.BannerUpsert req)
    {
        await _community.AdminSaveBannerAsync(HttpContext.User().Id, req);
        return Ok(ApiResponse<object>.Ok(null));
    }

    [HttpDelete("banners/{id}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteBanner(long id)
    {
        await _community.AdminDeleteBannerAsync(id);
        return Ok(ApiResponse<object>.Ok(null));
    }

    [HttpGet("posts")]
    public async Task<ActionResult<ApiResponse<Dtos.PagedResult<object>>>> Posts([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(ApiResponse<Dtos.PagedResult<object>>.Ok(await _community.AdminListPostsAsync(page, Math.Clamp(pageSize, 1, 50))));

    [HttpPost("posts/{id}/status/{status}")]
    public async Task<ActionResult<ApiResponse<object>>> SetPostStatus(long id, string status)
    {
        await _community.AdminSetPostStatusAsync(id, status);
        return Ok(ApiResponse<object>.Ok(null));
    }
}
