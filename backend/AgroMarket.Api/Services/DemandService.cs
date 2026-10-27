using System.Data;
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
        public bool Negotiable { get; set; }
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    private const string SelectSql = @"
        SELECT d.id, d.publisher_id PublisherId, u.nickname PublisherName, d.title, d.category_id CategoryId,
               c.name CategoryName, d.description, d.quantity, d.unit, d.price_min PriceMin, d.price_max PriceMax,
               d.price_unit PriceUnit, d.spec_required SpecRequired, d.county_id CountyId, d.town_id TownId, d.village_id VillageId,
               d.expect_date ExpectDate, d.packaging_note PackagingNote, d.contact_name ContactName,
               d.contact_phone ContactPhone, d.negotiable Negotiable, d.status, d.created_at CreatedAt
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

    private const string QuoteCountSql = @"
        SELECT q.demand_id DemandId, COUNT(*) QuoteCount
        FROM trade_quote q
        WHERE q.demand_id IN @ids AND q.status NOT IN ('REJECTED','CLOSED')
        GROUP BY q.demand_id";

    public async Task<Dtos.PagedResult<Dtos.DemandDto>> QueryAsync(Dtos.DemandQuery q, long viewerId = 0, bool admin = false)
    {
        if (q.Status == "MINE")
        {
            if (viewerId <= 0) throw new BizException("请先登录", ErrorCodes.Unauthorized);
            q.PublisherId = viewerId;
        }
        else if (!admin) q = q with { Status = "PUBLIC" };
        q.Page = Paging.Normalize(q.Page);
        if (q.PageSize is < 1 or > 50) q.PageSize = 10;
        var w = new List<string>();
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(q.Keyword))
        {
            // 标题 / 描述 / 品类名 任一命中
            w.Add("(d.title LIKE @kw OR d.description LIKE @kw OR c.name LIKE @kw)");
            p.Add("kw", $"%{q.Keyword.Trim()}%");
        }
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
        p.Add("limit", q.PageSize); p.Add("offset", Paging.Offset(q.Page, q.PageSize));
        var orderBy = q.OrderBy switch
        {
            // 区间报价统一按最低价升序/降序, 避免区间两端语义混乱
            "price_asc" => "COALESCE(d.price_min, 99999999.99) ASC, d.id DESC",
            "price_desc" => "COALESCE(d.price_min, 0) DESC, d.id DESC",
            "quantity" => "d.quantity DESC, d.id DESC",
            _ => "d.created_at DESC, d.id DESC",
        };
        // 用户常驻乡镇优先置顶(软排序, 不过滤)
        if (q.PreferTownId != null)
        {
            orderBy = "(d.town_id=@ptown) DESC, " + orderBy;
            p.Add("ptown", q.PreferTownId);
        }
        using var conn = _db.Create();
        var total = await conn.ExecuteScalarAsync<long>(string.Format(CountSql, where), p);
        var rows = (await conn.QueryAsync<DemandRow>(string.Format(SelectSql, where, orderBy), p)).ToList();
        var items = await AttachAsync(conn, rows, viewerId > 0 || admin);
        return new Dtos.PagedResult<Dtos.DemandDto>(items, total, q.Page, q.PageSize);
    }

    private async Task<List<Dtos.DemandDto>> AttachAsync(IDbConnection conn, List<DemandRow> rows, bool preciseLocation = true)
    {
        if (rows.Count == 0) return new();
        var ids = rows.Select(r => r.Id).ToList();
        var imgs = (await conn.QueryAsync<(long DemandId, long Id, string Url)>(
            "SELECT demand_id, id, url FROM product_image WHERE demand_id IN @ids ORDER BY sort_order, id",
            new { ids })).GroupBy(i => i.DemandId).ToDictionary(g => g.Key, g => g.ToList());
        var qcnt = new Dictionary<long, int>();
        foreach (var g in await conn.QueryAsync<(long DemandId, int QuoteCount)>(QuoteCountSql, new { ids }))
            qcnt[g.DemandId] = g.QuoteCount;
        var result = new List<Dtos.DemandDto>();
        foreach (var r in rows)
        {
            var (cName, tName, _) = await _regions.GetRegionNamesAsync(r.CountyId, r.TownId, r.VillageId);
            imgs.TryGetValue(r.Id, out var imgList);
            result.Add(new Dtos.DemandDto(r.Id, r.PublisherId, r.PublisherName, r.Title, r.CategoryId, r.CategoryName,
                r.Description, r.Quantity, r.Unit, r.PriceMin, r.PriceMax, r.PriceUnit, r.SpecRequired,
                // 与供应列表同口径: 乡镇/村级位置只给已登录用户
                r.CountyId, cName, preciseLocation ? r.TownId : null, preciseLocation ? tName : null,
                preciseLocation ? r.VillageId : null, r.ExpectDate, r.PackagingNote,
                r.ContactName, null, r.Status, r.CreatedAt, r.Negotiable,
                qcnt.TryGetValue(r.Id, out var qc) ? qc : 0,
                (imgList ?? new()).Select(i => new Dtos.DemandImageDto(i.Id, i.Url)).ToList()));
        }
        return result;
    }

    public async Task<Dtos.DemandDto> GetByIdAsync(long id, long? viewerId)
    {
        using var conn = _db.Create();
        var r = await conn.QueryFirstOrDefaultAsync<DemandRow>(string.Format(SelectSql, " AND d.id=@id", "d.id"), new { id, limit = 1, offset = 0 });
        if (r == null) throw new BizException("采购需求不存在", ErrorCodes.NotFound);
        // 可见性口径统一由 PhoneAccessPolicy 决定, 不在各 Service 里各写一遍
        var phoneVisible = await PhoneAccessPolicy.CanSeeAsync(conn, viewerId, r.PublisherId);
        if (r.Status != "ACTIVE" && !phoneVisible)
            throw new BizException("采购需求不存在", ErrorCodes.NotFound);
        var dto = (await AttachAsync(conn, new List<DemandRow> { r }, phoneVisible))[0];
        return dto with { ContactPhone = phoneVisible ? r.ContactPhone : null };
    }

    public async Task<long> CreateAsync(long userId, Dtos.DemandCreateRequest req)
    {
        Validate(req);
        await _regions.ValidateRegionAsync(req.CountyId, null, 3);
        await _regions.ValidateRegionAsync(req.TownId, req.CountyId, 4);
        await _regions.ValidateRegionAsync(req.VillageId, req.TownId, 5);
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var id = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO purchase_demand (publisher_id, title, category_id, description, quantity, unit, price_min, price_max,
                price_unit, spec_required, county_id, town_id, village_id, expect_date, packaging_note, contact_name,
                contact_phone, negotiable, status, effective_days, expire_at)
            VALUES (@pub, @title, @cat, @desc, @qty, @unit, @pmin, @pmax, @punit, @spec, @county, @town, @village,
                @expect, @pack, @cname, @cphone, @nego, 'PENDING', @days, DATE_ADD(NOW(3), INTERVAL @days DAY));
            SELECT LAST_INSERT_ID();",
            new { pub = userId, req.Title, cat = req.CategoryId, desc = req.Description, qty = req.Quantity, req.Unit,
                pmin = req.PriceMin, pmax = req.PriceMax, punit = req.PriceUnit, spec = req.SpecRequired,
                county = req.CountyId, town = req.TownId, village = req.VillageId, expect = req.ExpectDate, pack = req.PackagingNote,
                cname = req.ContactName, cphone = req.ContactPhone, nego = req.Negotiable, days = req.EffectiveDays }, tx);
            if (req.Images is { Count: > 0 })
                await conn.ExecuteAsync(
                    "INSERT INTO product_image (demand_id, url, sort_order) VALUES (@did, @url, @i)",
                    req.Images.Take(9).Select((url, i) => new { did = id, url, i }), tx);
            tx.Commit();
            return id;
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task UpdateAsync(long userId, long id, Dtos.DemandCreateRequest req)
    {
        using var conn0 = _db.Create();
        var isAdmin = await conn0.ExecuteScalarAsync<bool>("SELECT role='ADMIN' FROM sys_user WHERE id=@id", new { id = userId });
        var owner = await conn0.ExecuteScalarAsync<long?>("SELECT publisher_id FROM purchase_demand WHERE id=@id", new { id });
        if (owner == null) throw new BizException("采购需求不存在", ErrorCodes.NotFound);
        if (owner != userId && !isAdmin) throw new BizException("无权修改他人的采购需求", ErrorCodes.Forbidden);
        Validate(req);
        await _regions.ValidateRegionAsync(req.CountyId, null, 3);
        await _regions.ValidateRegionAsync(req.TownId, req.CountyId, 4);
        await _regions.ValidateRegionAsync(req.VillageId, req.TownId, 5);
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var affected = await conn.ExecuteAsync(@"
            UPDATE purchase_demand SET title=@title, category_id=@cat, description=@desc, quantity=@qty, unit=@unit,
                price_min=@pmin, price_max=@pmax, price_unit=@punit, spec_required=@spec, county_id=@county, town_id=@town,
                village_id=@village, expect_date=@expect, packaging_note=@pack, contact_name=@cname, contact_phone=@cphone,
                negotiable=@nego, effective_days=@days, expire_at=DATE_ADD(NOW(3), INTERVAL @days DAY), status='PENDING'
            WHERE id=@id",
            new { req.Title, cat = req.CategoryId, desc = req.Description, qty = req.Quantity, req.Unit, pmin = req.PriceMin,
                pmax = req.PriceMax, punit = req.PriceUnit, spec = req.SpecRequired, county = req.CountyId, town = req.TownId,
                village = req.VillageId, expect = req.ExpectDate, pack = req.PackagingNote, cname = req.ContactName,
                cphone = req.ContactPhone, nego = req.Negotiable, days = req.EffectiveDays, id }, tx);
            if (affected == 0) throw new BizException("更新失败");
            await conn.ExecuteAsync("DELETE FROM product_image WHERE demand_id=@id", new { id }, tx);
            if (req.Images is { Count: > 0 })
                await conn.ExecuteAsync(
                    "INSERT INTO product_image (demand_id, url, sort_order) VALUES (@did, @url, @i)",
                    req.Images.Take(9).Select((url, i) => new { did = id, url, i }), tx);
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
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
