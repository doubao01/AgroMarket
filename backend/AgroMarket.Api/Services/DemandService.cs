using AgroMarket.Api.Common;
using Dapper;

namespace AgroMarket.Api.Services;

/// <summary>采购需求业务。</summary>
public class DemandService
{
    private readonly IDbConnectionFactory _db;
    private readonly RegionService _regions;

    public DemandService(IDbConnectionFactory db, RegionService regions) { _db = db; _regions = regions; }

    public class DemandRow
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
        public decimal? PriceMin { get; set; }
        public decimal? PriceMax { get; set; }
        public string PriceUnit { get; set; } = "";
        public string? SpecRequired { get; set; }
        public long? CountyId { get; set; }
        public long? TownId { get; set; }
        public long? VillageId { get; set; }
        public DateTime? ExpectDate { get; set; }
        public string? PackagingNote { get; set; }
        public string? ContactName { get; set; }
        public string? ContactPhone { get; set; }
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    private const string SelectSql = @"
        SELECT d.id, d.publisher_id PublisherId, u.nickname PublisherName, d.title, d.category_id CategoryId,
               c.name CategoryName, d.description, d.quantity, d.unit, d.price_min PriceMin, d.price_max PriceMax,
               d.price_unit PriceUnit, d.spec_required SpecRequired, d.county_id CountyId, d.town_id TownId, d.village_id VillageId,
               d.expect_date ExpectDate, d.packaging_note PackagingNote, d.contact_name ContactName,
               d.contact_phone ContactPhone, d.status, d.created_at CreatedAt
        FROM purchase_demand d
        JOIN sys_user u ON u.id = d.publisher_id
        JOIN product_category c ON c.id = d.category_id
        WHERE 1=1 {0}
        ORDER BY {1}
        LIMIT @limit OFFSET @offset";

    private const string CountSql = @"
        SELECT COUNT(*) FROM purchase_demand d
        JOIN sys_user u ON u.id = d.publisher_id
        JOIN product_category c ON c.id = d.category_id
        WHERE 1=1 {0}";

    public async Task<Dtos.PagedResult<Dtos.DemandDto>> QueryAsync(Dtos.DemandQuery q, long viewerId = 0, bool admin = false)
    {
        if (q.Status == "MINE")
        {
            if (viewerId <= 0) throw new BizException("请先登录", ErrorCodes.Unauthorized);
            q.PublisherId = viewerId;
        }
        else if (!admin) q = q with { Status = "PUBLIC" };
        if (q.Page < 1) q.Page = 1;
        if (q.PageSize is < 1 or > 50) q.PageSize = 10;
        var w = new List<string>();
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(q.Keyword)) { w.Add("d.title LIKE @kw"); p.Add("kw", $"%{q.Keyword.Trim()}%"); }
        if (q.CategoryId != null) { w.Add("(d.category_id=@cat OR c.parent_id=@cat)"); p.Add("cat", q.CategoryId); }
        if (q.PriceMin != null) { w.Add("d.price_max >= @pmin"); p.Add("pmin", q.PriceMin); }
        if (q.PriceMax != null) { w.Add("d.price_min <= @pmax"); p.Add("pmax", q.PriceMax); }
        if (q.CountyId != null) { w.Add("d.county_id = @county"); p.Add("county", q.CountyId); }
        if (q.TownId != null)
        {
            if (q.TownIncludeVillages)
                w.Add("(d.town_id = @town OR d.village_id IN (SELECT id FROM sys_region WHERE parent_id = @town))");
            else w.Add("d.town_id = @town");
            p.Add("town", q.TownId);
        }
        if (q.VillageId != null) { w.Add("d.village_id = @village"); p.Add("village", q.VillageId); }
        if (q.PublisherId != null) { w.Add("d.publisher_id = @pub"); p.Add("pub", q.PublisherId); }
        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            if (q.Status == "PUBLIC") w.Add("d.status = 'ACTIVE'");
            else if (q.Status == "MINE")
            {
                w.Add("d.status IN ('PENDING','ACTIVE','CLOSED','OFF_SHELF','EXPIRED','VIOLATION')");
                if (q.PublisherId == null) w.Add("1=0");
            }
            else { w.Add("d.status = @st"); p.Add("st", q.Status); }
        }
        else w.Add("d.status = 'ACTIVE'");
        var where = w.Count == 0 ? "1=1" : " AND " + string.Join(" AND ", w);
        p.Add("limit", q.PageSize); p.Add("offset", (q.Page - 1) * q.PageSize);
        var orderBy = q.OrderBy switch
        {
            "price_asc" => "d.price_min ASC, d.id DESC",
            "price_desc" => "d.price_max DESC, d.id DESC",
            "quantity" => "d.quantity DESC, d.id DESC",
            _ => "d.created_at DESC, d.id DESC",
        };
        using var conn = _db.Create();
        var total = await conn.ExecuteScalarAsync<long>(string.Format(CountSql, where), p);
        var rows = (await conn.QueryAsync<DemandRow>(string.Format(SelectSql, where, orderBy), p)).ToList();
        var items = new List<Dtos.DemandDto>();
        foreach (var r in rows)
        {
            var (cName, tName, _) = await _regions.GetRegionNamesAsync(r.CountyId, r.TownId, r.VillageId);
            items.Add(new Dtos.DemandDto(r.Id, r.PublisherId, r.PublisherName, r.Title, r.CategoryId, r.CategoryName,
                r.Description, r.Quantity, r.Unit, r.PriceMin, r.PriceMax, r.PriceUnit, r.SpecRequired,
                r.CountyId, cName, r.TownId, tName, r.VillageId, r.ExpectDate, r.PackagingNote,
                r.ContactName, null, r.Status, r.CreatedAt));
        }
        return new Dtos.PagedResult<Dtos.DemandDto>(items, total, q.Page, q.PageSize);
    }

    public async Task<Dtos.DemandDto> GetByIdAsync(long id, long? viewerId)
    {
        using var conn = _db.Create();
        var r = await conn.QueryFirstOrDefaultAsync<DemandRow>(string.Format(SelectSql, " AND d.id=@id", "d.id"), new { id, limit = 1, offset = 0 });
        if (r == null) throw new BizException("采购需求不存在", ErrorCodes.NotFound);
        var isAdmin = viewerId != null && await conn.ExecuteScalarAsync<bool>("SELECT role='ADMIN' FROM sys_user WHERE id=@id", new { id = viewerId });
        var phoneVisible = viewerId != null && (viewerId == r.PublisherId || isAdmin);
        if (r.Status != "ACTIVE" && !phoneVisible)
            throw new BizException("采购需求不存在", ErrorCodes.NotFound);
        var (cName, tName, _) = await _regions.GetRegionNamesAsync(r.CountyId, r.TownId, r.VillageId);
        return new Dtos.DemandDto(r.Id, r.PublisherId, r.PublisherName, r.Title, r.CategoryId, r.CategoryName,
            r.Description, r.Quantity, r.Unit, r.PriceMin, r.PriceMax, r.PriceUnit, r.SpecRequired,
            r.CountyId, cName, r.TownId, tName, r.VillageId, r.ExpectDate, r.PackagingNote,
            r.ContactName, phoneVisible ? r.ContactPhone : null, r.Status, r.CreatedAt);
    }

    public async Task<long> CreateAsync(long userId, Dtos.DemandCreateRequest req)
    {
        Validate(req);
        await _regions.ValidateRegionAsync(req.CountyId, null, 3);
        await _regions.ValidateRegionAsync(req.TownId, req.CountyId, 4);
        await _regions.ValidateRegionAsync(req.VillageId, req.TownId, 5);
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO purchase_demand (publisher_id, title, category_id, description, quantity, unit, price_min, price_max,
                price_unit, spec_required, county_id, town_id, village_id, expect_date, packaging_note, contact_name,
                contact_phone, status, effective_days, expire_at)
            VALUES (@pub, @title, @cat, @desc, @qty, @unit, @pmin, @pmax, @punit, @spec, @county, @town, @village,
                @expect, @pack, @cname, @cphone, 'PENDING', @days, DATE_ADD(NOW(3), INTERVAL @days DAY));
            SELECT LAST_INSERT_ID();",
            new { pub = userId, req.Title, cat = req.CategoryId, desc = req.Description, qty = req.Quantity, req.Unit,
                pmin = req.PriceMin, pmax = req.PriceMax, punit = req.PriceUnit, spec = req.SpecRequired,
                county = req.CountyId, town = req.TownId, village = req.VillageId, expect = req.ExpectDate, pack = req.PackagingNote,
                cname = req.ContactName, cphone = req.ContactPhone, days = req.EffectiveDays });
    }

    public async Task UpdateAsync(long userId, long id, Dtos.DemandCreateRequest req)
    {
        using var conn = _db.Create();
        var isAdmin = await conn.ExecuteScalarAsync<bool>("SELECT role='ADMIN' FROM sys_user WHERE id=@id", new { id = userId });
        var owner = await conn.ExecuteScalarAsync<long?>("SELECT publisher_id FROM purchase_demand WHERE id=@id", new { id });
        if (owner == null) throw new BizException("采购需求不存在", ErrorCodes.NotFound);
        if (owner != userId && !isAdmin) throw new BizException("无权修改他人的采购需求", ErrorCodes.Forbidden);
        Validate(req);
        await _regions.ValidateRegionAsync(req.CountyId, null, 3);
        await _regions.ValidateRegionAsync(req.TownId, req.CountyId, 4);
        await _regions.ValidateRegionAsync(req.VillageId, req.TownId, 5);
        var affected = await conn.ExecuteAsync(@"
            UPDATE purchase_demand SET title=@title, category_id=@cat, description=@desc, quantity=@qty, unit=@unit,
                price_min=@pmin, price_max=@pmax, price_unit=@punit, spec_required=@spec, county_id=@county, town_id=@town,
                village_id=@village, expect_date=@expect, packaging_note=@pack, contact_name=@cname, contact_phone=@cphone,
                effective_days=@days, expire_at=DATE_ADD(NOW(3), INTERVAL @days DAY), status='PENDING'
            WHERE id=@id",
            new { req.Title, cat = req.CategoryId, desc = req.Description, qty = req.Quantity, req.Unit, pmin = req.PriceMin,
                pmax = req.PriceMax, punit = req.PriceUnit, spec = req.SpecRequired, county = req.CountyId, town = req.TownId,
                village = req.VillageId, expect = req.ExpectDate, pack = req.PackagingNote, cname = req.ContactName,
                cphone = req.ContactPhone, days = req.EffectiveDays, id });
        if (affected == 0) throw new BizException("更新失败");
    }

    public async Task ChangeStatusAsync(long userId, long id, string action)
    {
        using var conn = _db.Create();
        var isAdmin = await conn.ExecuteScalarAsync<bool>("SELECT role='ADMIN' FROM sys_user WHERE id=@id", new { id = userId });
        var owner = await conn.ExecuteScalarAsync<long?>("SELECT publisher_id FROM purchase_demand WHERE id=@id", new { id });
        if (owner == null) throw new BizException("采购需求不存在", ErrorCodes.NotFound);
        if (owner != userId && !isAdmin) throw new BizException("无权操作他人的采购需求", ErrorCodes.Forbidden);
        var target = action switch
        {
            "off_shelf" => "OFF_SHELF",
            "re_activate" => "PENDING",
            "close" => "CLOSED",
            _ => throw new BizException("不支持的操作"),
        };
        var affected = await conn.ExecuteAsync(
            "UPDATE purchase_demand SET status=@st WHERE id=@id" + (isAdmin ? "" : " AND publisher_id=@uid"),
            new { st = target, id, uid = userId });
        if (affected == 0) throw new BizException("操作失败, 状态可能已变更", ErrorCodes.Conflict);
    }

    private void Validate(Dtos.DemandCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Trim().Length < 2 || req.Title.Length > 60)
            throw new BizException("采购标题长度须在 2-60 字之间");
        if (req.Quantity <= 0) throw new BizException("采购数量必须大于 0");
        if (string.IsNullOrWhiteSpace(req.Unit)) throw new BizException("请选择计量单位");
        if (req.PriceMin != null && req.PriceMin <= 0) throw new BizException("目标价格必须大于 0");
        if (req.PriceMax != null && req.PriceMax <= 0) throw new BizException("目标价格必须大于 0");
        if (req.PriceMax != null && req.PriceMin != null && req.PriceMax < req.PriceMin)
            throw new BizException("价格上限不能低于下限");
        if (req.EffectiveDays is < 1 or > 365) throw new BizException("有效期须在 1-365 天");
    }
}
