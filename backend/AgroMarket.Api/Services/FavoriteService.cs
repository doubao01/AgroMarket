using AgroMarket.Api.Common;
using Dapper;

namespace AgroMarket.Api.Services;

public class FavoriteService
{
    private readonly IDbConnectionFactory _db;
    public FavoriteService(IDbConnectionFactory db) => _db = db;

    public async Task AddAsync(long userId, string targetType, long targetId)
    {
        if (targetType is not ("SUPPLY" or "DEMAND")) throw new BizException("收藏类型不合法");
        using var conn = _db.Create();
        if (targetType == "SUPPLY")
        {
            var ok = await conn.ExecuteScalarAsync<long?>("SELECT id FROM product_supply WHERE id=@id AND status='ACTIVE'", new { id = targetId });
            if (ok == null) throw new BizException("供应信息不存在或已下架", ErrorCodes.NotFound);
        }
        else
        {
            var ok = await conn.ExecuteScalarAsync<long?>("SELECT id FROM purchase_demand WHERE id=@id AND status='ACTIVE'", new { id = targetId });
            if (ok == null) throw new BizException("采购需求不存在或已关闭", ErrorCodes.NotFound);
        }
        await conn.ExecuteAsync(
            @"INSERT IGNORE INTO user_favorite (user_id, target_type, target_id) VALUES (@u, @t, @id)",
            new { u = userId, t = targetType, id = targetId });
    }

    public async Task RemoveAsync(long userId, string targetType, long targetId)
    {
        using var conn = _db.Create();
        await conn.ExecuteAsync(
            "DELETE FROM user_favorite WHERE user_id=@u AND target_type=@t AND target_id=@id",
            new { u = userId, t = targetType, id = targetId });
    }

    public async Task<Dtos.PagedResult<Dtos.FavoriteDto>> QueryAsync(long userId, string? targetType, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 10;
        var w = "f.user_id=@uid";
        if (targetType is "SUPPLY" or "DEMAND") w += " AND f.target_type=@tt";
        using var conn = _db.Create();
        var total = await conn.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM user_favorite f WHERE {w}",
            new { uid = userId, tt = targetType });
        var rows = (await conn.QueryAsync<(long Id, string TargetType, long TargetId, string? Title, decimal? Price, DateTime CreatedAt)>(
            $@"SELECT f.id, f.target_type TargetType, f.target_id TargetId,
                      COALESCE(s.title, d.title) Title, COALESCE(s.price, d.price_min) Price, f.created_at CreatedAt
               FROM user_favorite f
               LEFT JOIN product_supply s ON f.target_type='SUPPLY' AND s.id=f.target_id
               LEFT JOIN purchase_demand d ON f.target_type='DEMAND' AND d.id=f.target_id
               WHERE {w}
               ORDER BY f.id DESC LIMIT @limit OFFSET @offset",
            new { uid = userId, tt = targetType, limit = pageSize, offset = Paging.Offset(page, pageSize) })).ToList();
        return new Dtos.PagedResult<Dtos.FavoriteDto>(
            rows.Select(r => new Dtos.FavoriteDto(r.Id, r.TargetType, r.TargetId, r.Title, r.Price, r.CreatedAt)).ToList(),
            total, page, pageSize);
    }

    public async Task<bool> IsFavoritedAsync(long userId, string targetType, long targetId)
    {
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT COUNT(*) FROM user_favorite WHERE user_id=@u AND target_type=@t AND target_id=@id",
            new { u = userId, t = targetType, id = targetId });
    }
}
