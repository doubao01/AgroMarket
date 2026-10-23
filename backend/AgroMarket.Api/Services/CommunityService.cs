using AgroMarket.Api.Common;
using Dapper;
using System.Text.Json;
using AgroMarket.Api.Notifications;

namespace AgroMarket.Api.Services;

/// <summary>社区(发帖/评论/点赞)与焦点图。发帖即时可见, 管理端可隐藏; 评论走统一推送门面通知作者。</summary>
public class CommunityService
{
    private static readonly string[] Topics = { "CHAT", "MARKET", "HELP", "TRADE" };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _db;
    private readonly NotificationPushService _push;
    public CommunityService(IDbConnectionFactory db, NotificationPushService push) { _db = db; _push = push; }

    // ---------- 焦点图 ----------

    public async Task<List<BannerDto>> ListBannersAsync()
    {
        using var conn = _db.Create();
        return (await conn.QueryAsync<BannerDto>(
            "SELECT CAST(id AS SIGNED) Id, title Title, image_url ImageUrl, link_type LinkType, link_value LinkValue FROM banner WHERE enabled=1 ORDER BY sort_order, id")).ToList();
    }

    public async Task AdminSaveBannerAsync(long operatorId, BannerUpsert req)
    {
        using var conn = _db.Create();
        if (req.Id is > 0)
        {
            await conn.ExecuteAsync(
                "UPDATE banner SET title=@t, image_url=@img, link_type=@lt, link_value=@lv, sort_order=@so, enabled=@en WHERE id=@id",
                new { t = req.Title, img = req.ImageUrl, lt = req.LinkType, lv = req.LinkValue, so = req.SortOrder, en = req.Enabled, id = req.Id });
        }
        else
        {
            await conn.ExecuteAsync(
                "INSERT INTO banner (title, image_url, link_type, link_value, sort_order, enabled, created_by) VALUES (@t, @img, @lt, @lv, @so, @en, @by)",
                new { t = req.Title, img = req.ImageUrl, lt = req.LinkType, lv = req.LinkValue, so = req.SortOrder, en = req.Enabled, by = operatorId });
        }
    }

    public async Task AdminDeleteBannerAsync(long id)
    {
        using var conn = _db.Create();
        await conn.ExecuteAsync("DELETE FROM banner WHERE id=@id", new { id });
    }

    // ---------- 帖子 ----------

    public async Task<Dtos.PagedResult<PostDto>> ListPostsAsync(string? topic, string orderBy, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;
        var order = orderBy == "hot"
            ? "(p.like_count * 2 + p.comment_count) DESC, p.created_at DESC"
            : "p.created_at DESC";
        var where = "p.status='ACTIVE'" + (topic != null && Topics.Contains(topic) ? " AND p.topic=@tp" : "");
        using var conn = _db.Create();
        var total = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM community_post p WHERE {where}", new { tp = topic });
        var items = (await conn.QueryAsync<PostDto>(
            $@"SELECT CAST(p.id AS SIGNED) Id, CAST(p.user_id AS SIGNED) UserId, u.nickname Nickname, u.avatar_url AvatarUrl,
                      p.topic Topic, p.title Title, p.content Content, p.images Images,
                      p.like_count LikeCount, p.comment_count CommentCount, p.created_at CreatedAt
               FROM community_post p JOIN sys_user u ON u.id=p.user_id
               WHERE {where} ORDER BY {order} LIMIT @l OFFSET @o",
            new { tp = topic, l = pageSize, o = Paging.Offset(page, pageSize) })).ToList();
        return new Dtos.PagedResult<PostDto>(items, total, page, pageSize);
    }

    public async Task<object> GetPostAsync(long id, long? viewerId)
    {
        using var conn = _db.Create();
        var post = await conn.QuerySingleOrDefaultAsync<PostDto>(
            @"SELECT CAST(p.id AS SIGNED) Id, CAST(p.user_id AS SIGNED) UserId, u.nickname Nickname, u.avatar_url AvatarUrl,
                     p.topic Topic, p.title Title, p.content Content, p.images Images,
                     p.like_count LikeCount, p.comment_count CommentCount, p.created_at CreatedAt
              FROM community_post p JOIN sys_user u ON u.id=p.user_id
              WHERE p.id=@id AND p.status='ACTIVE'", new { id });
        if (post == null) throw new BizException("帖子不存在或已被隐藏", ErrorCodes.NotFound);
        var comments = (await conn.QueryAsync<CommentDto>(
            @"SELECT CAST(c.id AS SIGNED) Id, CAST(c.user_id AS SIGNED) UserId, u.nickname Nickname, u.avatar_url AvatarUrl,
                     CAST(c.parent_id AS SIGNED) ParentId, CAST(c.reply_user_id AS SIGNED) ReplyUserId, ru.nickname ReplyNickname,
                     c.content Content, c.created_at CreatedAt
              FROM community_comment c
              JOIN sys_user u ON u.id=c.user_id
              LEFT JOIN sys_user ru ON ru.id=c.reply_user_id
              WHERE c.post_id=@id AND c.status='ACTIVE' ORDER BY c.id", new { id })).ToList();
        var liked = false;
        if (viewerId != null)
            liked = await conn.ExecuteScalarAsync<bool>(
                "SELECT 1 FROM post_like WHERE post_id=@id AND user_id=@u", new { id, u = viewerId });
        return new { post, liked, comments };
    }

    public async Task<long> CreatePostAsync(long userId, PostUpsert req)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Trim().Length < 2) throw new BizException("标题至少 2 个字");
        if (string.IsNullOrWhiteSpace(req.Content) || req.Content.Trim().Length < 5) throw new BizException("正文至少 5 个字");
        if (req.Title.Length > 100) throw new BizException("标题过长");
        if (req.Content.Length > 10000) throw new BizException("正文过长");
        var topic = Topics.Contains(req.Topic) ? req.Topic : "CHAT";
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<long>(
            "INSERT INTO community_post (user_id, topic, title, content, images) VALUES (@u, @tp, @t, @c, @imgs); SELECT LAST_INSERT_ID();",
            new { u = userId, tp = topic, t = req.Title.Trim(), c = req.Content.Trim(), imgs = req.Images == null ? null : JsonSerializer.Serialize(req.Images, Json) });
    }

    public async Task DeletePostAsync(long userId, long id)
    {
        using var conn = _db.Create();
        conn.Open(); // Dapper 的 Create() 返回未打开的连接, BeginTransaction 前必须显式打开
        using var tx = conn.BeginTransaction();
        // 归属校验必须先于级联删除: 否则非作者调用虽然删不掉帖子, 却会把他人帖子下的评论与点赞全部清空
        var affected = await conn.ExecuteAsync(
            "DELETE FROM community_post WHERE id=@id AND user_id=@u", new { id, u = userId }, tx);
        if (affected == 0) throw new BizException("帖子不存在或无权删除", ErrorCodes.NotFound);
        await conn.ExecuteAsync("DELETE FROM community_comment WHERE post_id=@id", new { id }, tx);
        await conn.ExecuteAsync("DELETE FROM post_like WHERE post_id=@id", new { id }, tx);
        tx.Commit();
    }

    public async Task AdminSetPostStatusAsync(long id, string status)
    {
        if (status is not ("ACTIVE" or "OFF")) throw new BizException("状态只能是 ACTIVE/OFF", ErrorCodes.BadRequest);
        using var conn = _db.Create();
        var affected = await conn.ExecuteAsync("UPDATE community_post SET status=@s WHERE id=@id", new { s = status, id });
        if (affected == 0) throw new BizException("帖子不存在", ErrorCodes.NotFound);
    }

    // ---------- 评论与点赞 ----------

    public async Task<long> AddCommentAsync(long userId, long postId, CommentUpsert req)
    {
        if (string.IsNullOrWhiteSpace(req.Content) || req.Content.Trim().Length < 1) throw new BizException("评论内容不能为空");
        // 与 community_comment.content VARCHAR(1000) 对齐, 否则超长会触发 MySQL 1406 被兜成 500
        if (req.Content.Trim().Length > 1000) throw new BizException("评论内容过长", ErrorCodes.BadRequest);
        using var conn = _db.Create();
        var post = await conn.QuerySingleOrDefaultAsync<(long Id, long UserId)>(
            "SELECT id Id, user_id UserId FROM community_post WHERE id=@id AND status='ACTIVE'", new { id = postId });
        if (post.Id == 0) throw new BizException("帖子不存在或已被隐藏", ErrorCodes.NotFound);
        long? parentId = null, replyUserId = null;
        if (req.ParentId is > 0)
        {
            var parent = await conn.QuerySingleOrDefaultAsync<(long Id, long UserId, long PostId)>(
                "SELECT id Id, user_id UserId, post_id PostId FROM community_comment WHERE id=@pid AND post_id=@id AND status='ACTIVE'",
                new { pid = req.ParentId, id = postId });
            if (parent.Id == 0) throw new BizException("被回复的评论不存在", ErrorCodes.NotFound);
            parentId = parent.Id;
            replyUserId = parent.UserId;
        }
        var commentId = await conn.ExecuteScalarAsync<long>(
            "INSERT INTO community_comment (post_id, user_id, parent_id, reply_user_id, content) VALUES (@p, @u, @pa, @ru, @c); SELECT LAST_INSERT_ID();",
            new { p = postId, u = userId, pa = parentId, ru = replyUserId, c = req.Content.Trim() });
        await conn.ExecuteAsync("UPDATE community_post SET comment_count=comment_count+1 WHERE id=@p", new { p = postId });
        // 通知帖主(或被回复人), 评论者自己不通知
        var notifyTo = replyUserId ?? post.UserId;
        if (notifyTo != userId)
        {
            var (title, detail) = replyUserId != null
                ? ("收到回复", $"你在帖子「{await PostTitleAsync(conn, postId)}」中收到新回复")
                : ("帖子有新评论", $"你的帖子「{await PostTitleAsync(conn, postId)}」有新评论");
            try { await _push.NotifyAsync(notifyTo, userId, "SYSTEM", title, detail, postId); } catch { }
        }
        return commentId;
    }

    private static async Task<string> PostTitleAsync(System.Data.IDbConnection conn, long postId)
        => await conn.ExecuteScalarAsync<string>("SELECT title FROM community_post WHERE id=@id", new { id = postId }) ?? "";

    public async Task<bool> ToggleLikeAsync(long userId, long postId)
    {
        using var conn = _db.Create();
        // post_like 无外键, 必须先校验帖子存在且可见, 否则会写入指向不存在帖子的孤儿点赞
        var postExists = await conn.ExecuteScalarAsync<int?>(
            "SELECT 1 FROM community_post WHERE id=@p AND status='ACTIVE'", new { p = postId });
        if (postExists == null) throw new BizException("帖子不存在或已被隐藏", ErrorCodes.NotFound);
        // 用 DELETE 的影响行数判定"之前是否已点赞", 再按实际 INSERT 行数累加:
        // 并发点赞时 INSERT IGNORE 可能 0 行, 若无条件 +1 会让 like_count 虚高
        var removed = await conn.ExecuteAsync(
            "DELETE FROM post_like WHERE post_id=@p AND user_id=@u", new { p = postId, u = userId });
        if (removed > 0)
        {
            await conn.ExecuteAsync("UPDATE community_post SET like_count=GREATEST(like_count-1,0) WHERE id=@p", new { p = postId });
            return false;
        }
        var inserted = await conn.ExecuteAsync(
            "INSERT IGNORE INTO post_like (post_id, user_id) VALUES (@p, @u)", new { p = postId, u = userId });
        if (inserted > 0)
            await conn.ExecuteAsync("UPDATE community_post SET like_count=like_count+1 WHERE id=@p", new { p = postId });
        return true;
    }

    // ---------- 管理端帖子列表 ----------

    public async Task<Dtos.PagedResult<object>> AdminListPostsAsync(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;
        using var conn = _db.Create();
        var total = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM community_post");
        var items = (await conn.QueryAsync<object>(
            @"SELECT CAST(p.id AS SIGNED) Id, CAST(p.user_id AS SIGNED) UserId, u.nickname Nickname, p.topic Topic, p.title Title,
                     p.like_count LikeCount, p.comment_count CommentCount, p.status Status, p.created_at CreatedAt
              FROM community_post p JOIN sys_user u ON u.id=p.user_id
              ORDER BY p.id DESC LIMIT @l OFFSET @o",
            new { l = pageSize, o = Paging.Offset(page, pageSize) })).ToList();
        return new Dtos.PagedResult<object>(items, total, page, pageSize);
    }

    public record BannerDto(long Id, string Title, string ImageUrl, string LinkType, string? LinkValue);
    public record BannerUpsert(long? Id, string Title, string ImageUrl, string LinkType, string? LinkValue, int SortOrder, bool Enabled);
    public record PostUpsert(string Topic, string Title, string Content, List<string>? Images);
    public record CommentUpsert(string Content, long? ParentId);
    public record PostDto(long Id, long UserId, string Nickname, string? AvatarUrl, string Topic, string Title,
        string Content, string? Images, int LikeCount, int CommentCount, DateTime CreatedAt);
    public record CommentDto(long Id, long UserId, string Nickname, string? AvatarUrl, long? ParentId,
        long? ReplyUserId, string? ReplyNickname, string Content, DateTime CreatedAt);
}
