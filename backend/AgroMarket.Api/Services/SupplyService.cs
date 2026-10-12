using System.Data;
using AgroMarket.Api.Common;
using Dapper;

namespace AgroMarket.Api.Services;

/// <summary>供应信息业务, 含区域校验、状态流转与后端数据校验。</summary>
public class SupplyService
{
    private readonly IDbConnectionFactory _db;
    private readonly RegionService _regions;

    public SupplyService(IDbConnectionFactory db, RegionService regions) { _db = db; _regions = regions; }

    public class SupplyRow
    {
        public long Id { get; set; }
        public long PublisherId { get; set; }
        public string PublisherName { get; set; } = "";
        public string Title { get; set; } = "";
        public long CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public string? Description { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = "";
        public decimal? Price { get; set; }
        public string PriceUnit { get; set; } = "";
        public bool Negotiable { get; set; }
        public decimal? MinOrderQty { get; set; }
        public long? CountyId { get; set; }
        public long? TownId { get; set; }
        public long? VillageId { get; set; }
        public string? Spec { get; set; }
        public string? HarvestTime { get; set; }
        public string? DeliveryMode { get; set; }
        public string? ContactPhone { get; set; }
        public string? TradeNote { get; set; }
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    private const string SelectSql = @"
        SELECT s.id, s.publisher_id PublisherId, u.nickname PublisherName, s.title, s.category_id CategoryId,
               c.name CategoryName, s.description, s.quantity, s.unit, s.price, s.price_unit PriceUnit,
               s.negotiable, s.min_order_qty MinOrderQty, s.county_id CountyId, s.town_id TownId, s.village_id VillageId,
               s.spec, s.harvest_time HarvestTime, s.delivery_mode DeliveryMode, s.contact_phone ContactPhone,
               s.trade_note TradeNote, s.status, s.created_at CreatedAt
        FROM product_supply s
        JOIN sys_user u ON u.id = s.publisher_id
        JOIN product_category c ON c.id = s.category_id
        WHERE 1=1 {0}
        ORDER BY {1}
        LIMIT @limit OFFSET @offset";

    private const string CountSql = @"
        SELECT COUNT(*) FROM product_supply s
        JOIN sys_user u ON u.id = s.publisher_id
        JOIN product_category c ON c.id = s.category_id
        WHERE 1=1 {0}";

    private async Task<(string where, DynamicParameters param)> BuildWhereAsync(Dtos.SupplyQuery q)
    {
        var w = new List<string>();
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(q.Keyword)) { w.Add("s.title LIKE @kw"); p.Add("kw", $"%{q.Keyword.Trim()}%"); }
        if (q.CategoryId != null) { w.Add("(s.category_id=@cat OR c.parent_id=@cat)"); p.Add("cat", q.CategoryId); }
        if (q.PriceMin != null) { w.Add("s.price >= @pmin"); p.Add("pmin", q.PriceMin); }
        if (q.PriceMax != null) { w.Add("s.price <= @pmax"); p.Add("pmax", q.PriceMax); }
        if (q.CountyId != null) { w.Add("s.county_id = @county"); p.Add("county", q.CountyId); }
        if (q.TownId != null)
        {
            if (q.TownIncludeVillages)
            {
                w.Add("(s.town_id = @town OR s.village_id IN (SELECT id FROM sys_region WHERE parent_id = @town))");
            }
            else w.Add("s.town_id = @town");
            p.Add("town", q.TownId);
        }
        if (q.VillageId != null) { w.Add("s.village_id = @village"); p.Add("village", q.VillageId); }
        if (q.PublisherId != null) { w.Add("s.publisher_id = @pub"); p.Add("pub", q.PublisherId); }
        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            if (q.Status == "PUBLIC") { w.Add("s.status = 'ACTIVE'"); }
            else if (q.Status == "MINE")
            {
                w.Add("s.status IN ('PENDING','ACTIVE','SOLD_OUT','OFF_SHELF','EXPIRED','VIOLATION')");
                if (q.PublisherId == null) w.Add("1=0"); // 未登录时查 MINE 返回空
            }
            else { w.Add("s.status = @st"); p.Add("st", q.Status); }
        }
        else w.Add("s.status = 'ACTIVE'");
        return (w.Count == 0 ? "1=1" : " AND " + string.Join(" AND ", w), p);
    }

    public async Task<Dtos.PagedResult<Dtos.SupplyDto>> QueryAsync(Dtos.SupplyQuery q, long viewerId = 0, bool admin = false)
    {
        if (q.Status == "MINE")
        {
            if (viewerId <= 0) throw new BizException("请先登录", ErrorCodes.Unauthorized);
            q.PublisherId = viewerId;
        }
        else if (!admin) q = q with { Status = "PUBLIC" };
        if (q.Page < 1) q.Page = 1;
        if (q.PageSize is < 1 or > 50) q.PageSize = 10;
        var (where, p) = await BuildWhereAsync(q);
        p.Add("limit", q.PageSize);
        p.Add("offset", (q.Page - 1) * q.PageSize);
        var orderBy = q.OrderBy switch
        {
            "price_asc" => "s.price ASC, s.id DESC",
            "price_desc" => "s.price DESC, s.id DESC",
            "quantity" => "s.quantity DESC, s.id DESC",
            _ => "s.created_at DESC, s.id DESC",
        };
        using var conn = _db.Create();
        var total = await conn.ExecuteScalarAsync<long>(string.Format(CountSql, where), p);
        var rows = (await conn.QueryAsync<SupplyRow>(string.Format(SelectSql, where, orderBy), p)).ToList();
        var items = await AttachImagesAsync(conn, rows);
        return new Dtos.PagedResult<Dtos.SupplyDto>(items, total, q.Page, q.PageSize);
    }

    private async Task<List<Dtos.SupplyDto>> AttachImagesAsync(IDbConnection conn, List<SupplyRow> rows)
    {
        if (rows.Count == 0) return new();
        var ids = rows.Select(r => r.Id).ToList();
        var imgs = (await conn.QueryAsync<(long SupplyId, long Id, string Url)>(
            "SELECT supply_id, id, url FROM product_image WHERE supply_id IN @ids ORDER BY sort_order, id",
            new { ids })).GroupBy(i => i.SupplyId).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<Dtos.SupplyDto>();
        foreach (var r in rows)
        {
            var (cName, tName, vName) = await _regions.GetRegionNamesAsync(r.CountyId, r.TownId, r.VillageId);
            imgs.TryGetValue(r.Id, out var imgList);
            result.Add(new Dtos.SupplyDto(r.Id, r.PublisherId, r.PublisherName, r.Title, r.CategoryId, r.CategoryName,
                r.Description, r.Quantity, r.Unit, r.Price, r.PriceUnit, r.Negotiable, r.MinOrderQty,
                r.CountyId, cName, r.TownId, tName, r.VillageId, vName,
                r.Spec, r.HarvestTime, r.DeliveryMode,
                // 联系方式仅发布者本人可见, 其他人通过报价流程联系
                null, r.TradeNote, r.Status, r.CreatedAt,
                (imgList ?? new()).Select(i => new Dtos.SupplyImageDto(i.Id, i.Url)).ToList()));
        }
        return result;
    }

    public async Task<Dtos.SupplyDto> GetByIdAsync(long id, long? viewerId)
    {
        using var conn = _db.Create();
        var row = await conn.QueryFirstOrDefaultAsync<SupplyRow>(string.Format(SelectSql, " AND s.id=@id", "s.id"), new { id, limit = 1, offset = 0 });
        if (row == null) throw new BizException("供应信息不存在", ErrorCodes.NotFound);
        var isAdmin = viewerId != null && await conn.ExecuteScalarAsync<bool>("SELECT role='ADMIN' AND status='ACTIVE' FROM sys_user WHERE id=@id", new { id = viewerId });
        if (row.Status != "ACTIVE" && viewerId != row.PublisherId && !isAdmin)
            throw new BizException("供应信息不存在", ErrorCodes.NotFound);
        var list = await AttachImagesAsync(conn, new List<SupplyRow> { row });
        var dto = list[0];
        if (viewerId != null && viewerId == row.PublisherId)
            return dto with { ContactPhone = row.ContactPhone };
        return dto;
    }

    public async Task<long> CreateAsync(long userId, Dtos.SupplyCreateRequest req)
    {
        ValidateRequest(req);
        await _regions.ValidateRegionAsync(req.CountyId, null, 3);
        await _regions.ValidateRegionAsync(req.TownId, req.CountyId, 4);
        await _regions.ValidateRegionAsync(req.VillageId, req.TownId, 5);
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var id = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO product_supply (publisher_id, title, category_id, description, quantity, unit, price, price_unit,
                    negotiable, min_order_qty, county_id, town_id, village_id, spec, harvest_time, delivery_mode,
                    contact_phone, trade_note, status, effective_days, expire_at)
                VALUES (@pub, @title, @cat, @desc, @qty, @unit, @price, @punit, @nego, @moq, @county, @town, @village,
                    @spec, @harvest, @delivery, @phone, @note, 'PENDING', @days, DATE_ADD(NOW(3), INTERVAL @days DAY));
                SELECT LAST_INSERT_ID();",
                new { pub = userId, req.Title, cat = req.CategoryId, desc = req.Description, qty = req.Quantity, req.Unit, req.Price, punit = req.PriceUnit,
                    nego = req.Negotiable, moq = req.MinOrderQty, county = req.CountyId, town = req.TownId, village = req.VillageId, req.Spec, harvest = req.HarvestTime,
                    delivery = req.DeliveryMode, phone = req.ContactPhone, note = req.TradeNote, days = req.EffectiveDays }, tx);
            if (req.Images is { Count: > 0 })
                await conn.ExecuteAsync(
                    "INSERT INTO product_image (supply_id, url, sort_order) VALUES (@sid, @url, @i)",
                    req.Images.Take(9).Select((url, i) => new { sid = id, url, i }), tx);
            tx.Commit();
            return id;
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task UpdateAsync(long userId, long id, Dtos.SupplyCreateRequest req)
    {
        await EnsureOwnerAsync(userId, id);
        ValidateRequest(req);
        await _regions.ValidateRegionAsync(req.CountyId, null, 3);
        await _regions.ValidateRegionAsync(req.TownId, req.CountyId, 4);
        await _regions.ValidateRegionAsync(req.VillageId, req.TownId, 5);
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var affected = await conn.ExecuteAsync(@"
                UPDATE product_supply SET title=@title, category_id=@cat, description=@desc, quantity=@qty, unit=@unit,
                    price=@price, price_unit=@punit, negotiable=@nego, min_order_qty=@moq, county_id=@county, town_id=@town,
                    village_id=@village, spec=@spec, harvest_time=@harvest, delivery_mode=@delivery, contact_phone=@phone,
                    trade_note=@note, effective_days=@days, expire_at=DATE_ADD(NOW(3), INTERVAL @days DAY), status='PENDING'
                WHERE id=@id AND publisher_id=@pub",
                new { req.Title, cat = req.CategoryId, desc = req.Description, qty = req.Quantity, req.Unit, req.Price, punit = req.PriceUnit,
                    nego = req.Negotiable, moq = req.MinOrderQty, county = req.CountyId, town = req.TownId, village = req.VillageId, req.Spec, harvest = req.HarvestTime,
                    delivery = req.DeliveryMode, phone = req.ContactPhone, note = req.TradeNote, days = req.EffectiveDays, id, pub = userId }, tx);
            if (affected == 0) throw new BizException("供应信息不存在或无权修改", ErrorCodes.NotFound);
            await conn.ExecuteAsync("DELETE FROM product_image WHERE supply_id=@id", new { id }, tx);
            if (req.Images is { Count: > 0 })
                await conn.ExecuteAsync(
                    "INSERT INTO product_image (supply_id, url, sort_order) VALUES (@sid, @url, @i)",
                    req.Images.Take(9).Select((url, i) => new { sid = id, url, i }), tx);
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>用户自有状态流转: 下架/重新上架/标记售罄。已过期和违规由管理员处理。</summary>
    public async Task ChangeStatusAsync(long userId, long id, string action)
    {
        var owner = await GetOwnerAsync(id);
        using var conn0 = _db.Create();
        var isAdmin = await conn0.ExecuteScalarAsync<bool>("SELECT role='ADMIN' FROM sys_user WHERE id=@id", new { id = userId });
        if (owner != userId && !isAdmin) throw new BizException("无权操作他人的供应信息", ErrorCodes.Forbidden);
        var target = action switch
        {
            "off_shelf" => "OFF_SHELF",
            "re_activate" => "PENDING",
            "sold_out" => "SOLD_OUT",
            _ => throw new BizException("不支持的操作"),
        };
        using var conn = _db.Create();
        var affected = await conn.ExecuteAsync(
            "UPDATE product_supply SET status=@st WHERE id=@id" + (isAdmin ? "" : " AND publisher_id=@uid"),
            new { st = target, id, uid = userId });
        if (affected == 0) throw new BizException("操作失败, 状态可能已变更", ErrorCodes.Conflict);
    }

    public async Task<long> GetOwnerAsync(long id)
    {
        using var conn = _db.Create();
        var owner = await conn.ExecuteScalarAsync<long?>("SELECT publisher_id FROM product_supply WHERE id=@id", new { id });
        return owner ?? throw new BizException("供应信息不存在", ErrorCodes.NotFound);
    }

    private void ValidateRequest(Dtos.SupplyCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Trim().Length < 2 || req.Title.Length > 60)
            throw new BizException("农产品名称长度须在 2-60 字之间");
        if (req.Quantity <= 0) throw new BizException("供应数量必须大于 0");
        if (string.IsNullOrWhiteSpace(req.Unit)) throw new BizException("请选择计量单位");
        if (req.Price != null && req.Price <= 0) throw new BizException("参考单价必须大于 0");
        if (req.MinOrderQty != null && req.MinOrderQty <= 0) throw new BizException("最小起订量必须大于 0");
        if (req.MinOrderQty > req.Quantity) throw new BizException("最小起订量不能大于供应数量");
        if (req.EffectiveDays is < 1 or > 365) throw new BizException("有效期须在 1-365 天");
        if (req.CountyId == null) throw new BizException("请选择产地县域");
        if (req.TownId == null) throw new BizException("请选择产地乡镇");
    }

    private async Task EnsureOwnerAsync(long userId, long id)
    {
        using var conn = _db.Create();
        var isAdmin = await conn.ExecuteScalarAsync<bool>("SELECT role='ADMIN' FROM sys_user WHERE id=@id", new { id = userId });
        var owner = await conn.ExecuteScalarAsync<long?>("SELECT publisher_id FROM product_supply WHERE id=@id", new { id });
        if (owner == null) throw new BizException("供应信息不存在", ErrorCodes.NotFound);
        if (owner != userId && !isAdmin) throw new BizException("无权修改他人的供应信息", ErrorCodes.Forbidden);
    }
}
