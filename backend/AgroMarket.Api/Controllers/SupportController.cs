using AgroMarket.Api.Common;
using Dapper;
using AgroMarket.Api.Dtos;
using AgroMarket.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Controllers;

[ApiController]
[Route("api/favorites")]
public class FavoriteController : ControllerBase
{
    private readonly FavoriteService _favs;
    public FavoriteController(FavoriteService favs) => _favs = favs;

    [HttpGet]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<PagedResult<FavoriteDto>>>> Query(
        [FromQuery] string? targetType, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(ApiResponse<PagedResult<FavoriteDto>>.Ok(
            await _favs.QueryAsync(HttpContext.User().Id, targetType, page, pageSize)));

    [HttpPost("{targetType}/{targetId}")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> Add(string targetType, long targetId)
    {
        await _favs.AddAsync(HttpContext.User().Id, targetType, targetId);
        return Ok(ApiResponse<object>.Ok(null, "已收藏"));
    }

    [HttpDelete("{targetType}/{targetId}")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> Remove(string targetType, long targetId)
    {
        await _favs.RemoveAsync(HttpContext.User().Id, targetType, targetId);
        return Ok(ApiResponse<object>.Ok(null, "已取消收藏"));
    }

    [HttpGet("check")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<bool>>> Check([FromQuery] string targetType, [FromQuery] long targetId)
        => Ok(ApiResponse<bool>.Ok(await _favs.IsFavoritedAsync(HttpContext.User().Id, targetType, targetId)));
}

[ApiController]
[Route("api/messages")]
public class MessageController : ControllerBase
{
    private readonly MessageService _msgs;
    public MessageController(MessageService msgs) => _msgs = msgs;

    [HttpGet]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<PagedResult<MessageDto>>>> Query(
        [FromQuery] string? box, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(ApiResponse<PagedResult<MessageDto>>.Ok(
            await _msgs.QueryAsync(HttpContext.User().Id, box, page, pageSize)));

    [HttpPost("{id}/read")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> MarkRead(long id)
    {
        await _msgs.MarkReadAsync(HttpContext.User().Id, id);
        return Ok(ApiResponse<object>.Ok(null));
    }

    [HttpPost("read-all")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> MarkAllRead()
    {
        await _msgs.MarkAllReadAsync(HttpContext.User().Id);
        return Ok(ApiResponse<object>.Ok(null, "全部已读"));
    }

    [HttpGet("unread-count")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<long>>> UnreadCount()
        => Ok(ApiResponse<long>.Ok(await _msgs.UnreadCountAsync(HttpContext.User().Id)));
}

[ApiController]
[Route("api/addresses")]
public class AddressController : ControllerBase
{
    private readonly AddressService _addrs;
    public AddressController(AddressService addrs) => _addrs = addrs;

    [HttpGet]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<List<AddressDto>>>> Query()
        => Ok(ApiResponse<List<AddressDto>>.Ok(await _addrs.QueryAsync(HttpContext.User().Id)));

    [HttpPost]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<IdRequest>>> Create([FromBody] AddressRequest req)
        => Ok(ApiResponse<IdRequest>.Ok(new IdRequest(await _addrs.CreateAsync(HttpContext.User().Id, req)), "地址已保存"));

    [HttpDelete("{id}")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> Delete(long id)
    {
        await _addrs.DeleteAsync(HttpContext.User().Id, id);
        return Ok(ApiResponse<object>.Ok(null, "已删除"));
    }
}

[ApiController]
[Route("api/reports")]
public class ReportController : ControllerBase
{
    private readonly IDbConnectionFactory _db;
    private readonly MessageService _msgs;
    public ReportController(IDbConnectionFactory db, MessageService msgs) { _db = db; _msgs = msgs; }

    /// <summary>提交举报: targetType = SUPPLY/DEMAND/USER/QUOTE</summary>
    [HttpPost]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<IdRequest>>> Create([FromBody] ReportCreateRequest req)
    {
        if (req.TargetType is not ("SUPPLY" or "DEMAND" or "USER" or "QUOTE"))
            throw new BizException("举报类型不合法");
        if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Length < 5)
            throw new BizException("请填写举报原因 (至少 5 字)");
        using var conn = _db.Create();
        var id = await conn.ExecuteScalarAsync<long>(
            "INSERT INTO report_complaint (reporter_id, target_type, target_id, reason) VALUES (@r, @t, @tid, @reason); SELECT LAST_INSERT_ID();",
            new { r = HttpContext.User().Id, t = req.TargetType, tid = req.TargetId, reason = req.Reason.Trim() });
        await _msgs.SendAsync(1, HttpContext.User().Id, "REPORT", "新举报待处理",
            $"{req.TargetType} #{req.TargetId} 被举报: {req.Reason}", id);
        return Ok(ApiResponse<IdRequest>.Ok(new IdRequest(id), "举报已提交, 管理员会尽快处理"));
    }
}

[ApiController]
[Route("api/notices")]
public class NoticeController : ControllerBase
{
    private readonly IDbConnectionFactory _db;
    public NoticeController(IDbConnectionFactory db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<NoticeDto>>>> List()
    {
        using var conn = _db.Create();
        var items = (await conn.QueryAsync<NoticeDto>(
            "SELECT id, title, content Content, is_published IsPublished, created_at CreatedAt FROM sys_notice WHERE is_published=1 ORDER BY id DESC LIMIT 20")).ToList();
        return Ok(ApiResponse<List<NoticeDto>>.Ok(items));
    }
}

[ApiController]
[Route("api/files")]
public class FileController : ControllerBase
{
    private readonly StorageService _storage;
    public FileController(StorageService storage) => _storage = storage;

    /// <summary>单文件上传(小文件直传)。</summary>
    [HttpPost("upload")]
    [RequireAuth]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<object>>> Upload(IFormFile file, [FromQuery] string? purpose)
    {
        // purpose=private 用于商家证件等不该公开的材料: 文件落 .private/, 对外只有签名地址
        var isPrivate = string.Equals(purpose, "private", StringComparison.OrdinalIgnoreCase);
        var url = await _storage.SaveImageAsync(HttpContext.User().Id, file, isPrivate);
        return Ok(ApiResponse<object>.Ok(
            new { url, viewUrl = isPrivate ? _storage.SignPrivateUrl(url) : url }, "上传成功"));
    }

    /// <summary>
    /// 取私有文件(商家证件等)。仅接受带有效签名的地址, 且签名与路径、有效期绑定。
    /// 不用登录态判断: 小程序与 image 组件无法给图片请求附带 Authorization。
    /// </summary>
    [HttpGet("private/{date}/{name}")]
    public IActionResult GetPrivate(string date, string name, [FromQuery] long? e, [FromQuery] string? s)
    {
        var url = $"/api/files/private/{date}/{name}";
        // 签名无效/过期一律按"不存在"处理, 不区分原因, 避免被用来探测哪些文件存在
        if (!_storage.VerifyPrivateUrl(url, e?.ToString(), s)) return NotFound();
        var path = _storage.ResolvePrivatePath(url);
        if (path == null || !System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, max-age=300";
        return PhysicalFile(path, StorageService.ContentTypeOf(path));
    }

    /// <summary>删除自己上传的文件(更换头像/撤回资料后清理)。</summary>
    [HttpDelete("upload")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> DeleteUpload([FromQuery] string url)
    {
        await _storage.DeleteByUrlAsync(HttpContext.User().Id, url);
        return Ok(ApiResponse<object>.Ok(null, "已删除"));
    }

    /// <summary>初始化分片上传会话, 返回分片大小与总分片数。</summary>
    [HttpPost("upload/init")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<UploadInitResult>>> UploadInit([FromBody] UploadInitRequest req)
    {
        var r = await _storage.InitUploadAsync(HttpContext.User().Id, req.FileName, req.FileSize);
        return Ok(ApiResponse<UploadInitResult>.Ok(r, "已创建上传会话"));
    }

    /// <summary>查询已上传分片, 用于断点续传。</summary>
    [HttpGet("upload/status")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<UploadStatus>>> UploadStatus([FromQuery] string uploadId)
    {
        var r = await _storage.GetStatusAsync(HttpContext.User().Id, uploadId);
        return Ok(ApiResponse<UploadStatus>.Ok(r));
    }

    /// <summary>上传单个分片。</summary>
    [HttpPost("upload/chunk")]
    [RequireAuth]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<object>>> UploadChunk([FromForm] string uploadId, [FromForm] int index, IFormFile file)
    {
        var uploaded = await _storage.SaveChunkAsync(HttpContext.User().Id, uploadId, index, file);
        return Ok(ApiResponse<object>.Ok(new { uploadedChunks = uploaded }, "分片上传成功"));
    }

    /// <summary>合并分片完成上传, 返回最终文件地址。</summary>
    [HttpPost("upload/complete")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> UploadComplete([FromBody] UploadCompleteRequest req)
    {
        var url = await _storage.CompleteUploadAsync(HttpContext.User().Id, req.UploadId);
        return Ok(ApiResponse<object>.Ok(new { url }, "上传成功"));
    }
}
