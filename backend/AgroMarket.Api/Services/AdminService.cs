using System.Data;
using AgroMarket.Api.Common;
using Dapper;

namespace AgroMarket.Api.Services;

public class AdminService
{
    private readonly IDbConnectionFactory _db;
    private readonly RegionService _regions;

    public AdminService(IDbConnectionFactory db, RegionService regions) { _db = db; _regions = regions; }

    private static async Task EnsureAdminAsync(IDbConnection conn, long userId)
    {
        var isAdmin = await conn.ExecuteScalarAsync<bool>("SELECT role='ADMIN' FROM sys_user WHERE id=@id", new { id = userId });
        if (!isAdmin) throw new BizException("需要管理员权限", ErrorCodes.Forbidden);
    }

    public async Task LogAsync(long? operatorId, string module, string action, long? targetId, string? detail, string? ip)
    {
        try
        {
            using var conn = _db.Create();
            await conn.ExecuteAsync(
                "INSERT INTO sys_operation_log (operator_id, module, action, target_id, detail, ip) VALUES (@o, @m, @a, @t, @d, @ip)",
                new { o = operatorId, m = module, a = action, t = targetId, d = detail, ip });
        }
        catch { /* 日志失败不阻断业务 */ }
    }

    // ---------- 用户管理 ----------
    public async Task<Dtos.PagedResult<object>> QueryUsersAsync(long operatorId, string? keyword, string? role, string? status, int page, int pageSize)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 10;
        var w = new List<string>();
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(keyword)) { w.Add("(phone LIKE @kw OR nickname LIKE @kw)"); p.Add("kw", $"%{keyword}%"); }
        if (!string.IsNullOrWhiteSpace(role)) { w.Add("role=@r"); p.Add("r", role); }
        if (!string.IsNullOrWhiteSpace(status)) { w.Add("status=@s"); p.Add("s", status); }
        var where = w.Count > 0 ? "WHERE " + string.Join(" AND ", w) : "";
        p.Add("limit", pageSize); p.Add("offset", (page - 1) * pageSize);
        var total = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM sys_user {where}", p);
        var items = (await conn.QueryAsync<object>(
            $@"SELECT id, phone, nickname, role, status, home_region_id HomeRegionId, created_at CreatedAt, last_login_at LastLoginAt
               FROM sys_user {where} ORDER BY id DESC LIMIT @limit OFFSET @offset", p)).ToList();
        return new Dtos.PagedResult<object>(items, total, page, pageSize);
    }

    public async Task FreezeUserAsync(long operatorId, long userId, bool freeze)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        var affected = await conn.ExecuteAsync("UPDATE sys_user SET status=@s WHERE id=@id AND role != 'ADMIN'",
            new { s = freeze ? "FROZEN" : "ACTIVE", id = userId });
        if (affected == 0) throw new BizException("用户不存在或不可操作", ErrorCodes.NotFound);
        await LogAsync(operatorId, "USER", freeze ? "FREEZE" : "UNFREEZE", userId, null, null);
    }

    // ---------- 认证审核 ----------
    public async Task<Dtos.PagedResult<object>> QueryAuthsAsync(long operatorId, string? status, int page, int pageSize)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 10;
        var p = new DynamicParameters();
        var where = "";
        if (!string.IsNullOrWhiteSpace(status)) { where = "WHERE mp.audit_status=@s"; p.Add("s", status); }
        p.Add("limit", pageSize); p.Add("offset", (page - 1) * pageSize);
        var total = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM merchant_profile mp {where}", p);
        var items = (await conn.QueryAsync<object>(
            $@"SELECT mp.id, mp.user_id UserId, u.nickname, u.phone, mp.subject_type SubjectType, mp.subject_name SubjectName,
                      mp.id_number IdNumber, mp.license_url LicenseUrl, mp.audit_status AuditStatus, mp.created_at CreatedAt
               FROM merchant_profile mp JOIN sys_user u ON u.id = mp.user_id {where}
               ORDER BY mp.id DESC LIMIT @limit OFFSET @offset", p)).ToList();
        return new Dtos.PagedResult<object>(items, total, page, pageSize);
    }

    public async Task AuditAuthAsync(long operatorId, long authId, bool approved, string? remark)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        var affected = await conn.ExecuteAsync(
            "UPDATE merchant_profile SET audit_status=@s, audit_remark=@r, audited_by=@op, audited_at=NOW(3) WHERE id=@id AND audit_status='PENDING'",
            new { s = approved ? "APPROVED" : "REJECTED", r = remark, op = operatorId, id = authId });
        if (affected == 0) throw new BizException("认证申请不存在或已处理", ErrorCodes.Conflict);
        await LogAsync(operatorId, "AUTH", approved ? "APPROVE" : "REJECT", authId, remark, null);
    }

    // ---------- 供应/采购审核 ----------
    public async Task AuditSupplyAsync(long operatorId, long supplyId, bool approve, string? reason)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        var target = approve ? "ACTIVE" : "VIOLATION";
        var affected = await conn.ExecuteAsync(
            "UPDATE product_supply SET status=@st WHERE id=@id AND status='PENDING'",
            new { st = target, id = supplyId });
        if (affected == 0) throw new BizException("供应信息不存在或已审核", ErrorCodes.Conflict);
        var publisher = await conn.ExecuteScalarAsync<long>("SELECT publisher_id FROM product_supply WHERE id=@id", new { id = supplyId });
        await LogAsync(operatorId, "SUPPLY", target, supplyId, reason, null);
    }

    public async Task AuditDemandAsync(long operatorId, long demandId, bool approve, string? reason)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        var target = approve ? "ACTIVE" : "VIOLATION";
        var affected = await conn.ExecuteAsync(
            "UPDATE purchase_demand SET status=@st WHERE id=@id AND status='PENDING'",
            new { st = target, id = demandId });
        if (affected == 0) throw new BizException("采购需求不存在或已审核", ErrorCodes.Conflict);
        await LogAsync(operatorId, "DEMAND", target, demandId, reason, null);
    }

    // ---------- 区域管理 ----------
    public async Task<object> CreateRegionAsync(long operatorId, string code, string name, long? parentId, int level)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        if (parentId != null)
        {
            var parent = await conn.QueryFirstOrDefaultAsync<(long Id, int Level)>(
                "SELECT id, region_level FROM sys_region WHERE id=@id", new { id = parentId });
            if (parent.Id == 0) throw new BizException("上级区域不存在", ErrorCodes.NotFound);
            if (level != parent.Level + 1) throw new BizException("区域层级必须为上级+1");
        }
        else if (level != 1) throw new BizException("根区域必须为省级");
        try
        {
            var id = await conn.ExecuteScalarAsync<long>(
                "INSERT INTO sys_region (region_code, parent_id, region_name, region_level) VALUES (@c, @p, @n, @l); SELECT LAST_INSERT_ID();",
                new { c = code, p = parentId, n = name, l = level });
            await LogAsync(operatorId, "REGION", "CREATE", id, name, null);
            return new { id, code, name, parentId, level };
        }
        catch (Exception e) when (e.Message.Contains("Duplicate"))
        {
            throw new BizException("区域编码已存在", ErrorCodes.Conflict);
        }
    }

    public async Task SetRegionEnabledAsync(long operatorId, long regionId, bool enabled)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        var affected = await conn.ExecuteAsync("UPDATE sys_region SET is_enabled=@e WHERE id=@id", new { e = enabled, id = regionId });
        if (affected == 0) throw new BizException("区域不存在", ErrorCodes.NotFound);
        await LogAsync(operatorId, "REGION", enabled ? "ENABLE" : "DISABLE", regionId, null, null);
    }

    public async Task RenameRegionAsync(long operatorId, long regionId, string newName)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        // 历史数据通过 region_id 关联, 改名不影响历史记录
        var affected = await conn.ExecuteAsync("UPDATE sys_region SET region_name=@n WHERE id=@id", new { n = newName, id = regionId });
        if (affected == 0) throw new BizException("区域不存在", ErrorCodes.NotFound);
        await LogAsync(operatorId, "REGION", "RENAME", regionId, newName, null);
    }

    // ---------- 举报处理 ----------
    public async Task<Dtos.PagedResult<Dtos.ReportDto>> QueryReportsAsync(long operatorId, string? status, int page, int pageSize)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 10;
        var p = new DynamicParameters();
        var where = "";
        if (!string.IsNullOrWhiteSpace(status)) { where = "WHERE status=@s"; p.Add("s", status); }
        p.Add("limit", pageSize); p.Add("offset", (page - 1) * pageSize);
        var total = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM report_complaint {where}", p);
        var items = (await conn.QueryAsync<Dtos.ReportDto>(
            $@"SELECT id, reporter_id ReporterId, target_type TargetType, target_id TargetId, reason Reason,
                      status Status, handle_remark HandleRemark, created_at CreatedAt
               FROM report_complaint {where} ORDER BY id DESC LIMIT @limit OFFSET @offset", p)).ToList();
        return new Dtos.PagedResult<Dtos.ReportDto>(items, total, page, pageSize);
    }

    public async Task HandleReportAsync(long operatorId, long reportId, bool resolve, string? remark)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        var affected = await conn.ExecuteAsync(
            "UPDATE report_complaint SET status=@s, handle_remark=@r, handled_by=@op, handled_at=NOW(3) WHERE id=@id AND status='PENDING'",
            new { s = resolve ? "RESOLVED" : "REJECTED", r = remark, op = operatorId, id = reportId });
        if (affected == 0) throw new BizException("举报不存在或已处理", ErrorCodes.Conflict);
        await LogAsync(operatorId, "REPORT", resolve ? "RESOLVE" : "REJECT", reportId, remark, null);
    }

    // ---------- 公告 ----------
    public async Task<long> CreateNoticeAsync(long operatorId, string title, string content)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        return await conn.ExecuteScalarAsync<long>(
            "INSERT INTO sys_notice (title, content, is_published, publisher_id) VALUES (@t, @c, 1, @p); SELECT LAST_INSERT_ID();",
            new { t = title, c = content, p = operatorId });
    }

    public async Task DeleteNoticeAsync(long operatorId, long id)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        await conn.ExecuteAsync("UPDATE sys_notice SET is_published=0 WHERE id=@id", new { id });
        await LogAsync(operatorId, "NOTICE", "UNPUBLISH", id, null, null);
    }

    // ---------- 统计 ----------
    public async Task<Dtos.StatsDto> GetStatsAsync(long operatorId)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        var users = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sys_user");
        var activeUsers = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sys_user WHERE last_login_at > DATE_SUB(NOW(3), INTERVAL 30 DAY)");
        var supplies = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM product_supply WHERE status='ACTIVE'");
        var demands = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM purchase_demand WHERE status='ACTIVE'");
        // 真实成交: 仅统计已完成、非演示数据、且有真实支付渠道成功记录的订单
        var realOrders = await conn.ExecuteScalarAsync<long>(
            @"SELECT COUNT(DISTINCT o.id) FROM trade_order o
              WHERE o.status='COMPLETED' AND o.is_demo=0 AND EXISTS (
                  SELECT 1 FROM payment_record p
                  WHERE p.order_id=o.id AND p.status='SUCCESS' AND p.channel NOT IN ('MOCK','OFFLINE','DEMO'))");
        var realAmount = await conn.ExecuteScalarAsync<decimal>(
            @"SELECT COALESCE(SUM(o.total_amount),0) FROM trade_order o
              WHERE o.status='COMPLETED' AND o.is_demo=0 AND EXISTS (
                  SELECT 1 FROM payment_record p
                  WHERE p.order_id=o.id AND p.status='SUCCESS' AND p.channel NOT IN ('MOCK','OFFLINE','DEMO'))");
        // 演示/测试成交: 完成但为演示数据(开发环境自建订单), 单独统计避免计入真实成交额
        var demoOrders = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM trade_order WHERE status='COMPLETED' AND is_demo=1");
        var demoAmount = await conn.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(total_amount),0) FROM trade_order WHERE status='COMPLETED' AND is_demo=1");
        var townStats = (await conn.QueryAsync<Dtos.TownStat>(
            @"SELECT r.id TownId, r.region_name TownName,
                     (SELECT COUNT(*) FROM product_supply s WHERE s.town_id=r.id AND s.status='ACTIVE') SupplyCount,
                     (SELECT COUNT(*) FROM purchase_demand d WHERE d.town_id=r.id AND d.status='ACTIVE') DemandCount
              FROM sys_region r WHERE r.region_level=4 AND r.parent_id=3 AND r.is_enabled=1 ORDER BY r.sort_order")).ToList();
        return new Dtos.StatsDto(users, activeUsers, supplies, demands, realOrders, realAmount, townStats, demoOrders, demoAmount);
    }
}
