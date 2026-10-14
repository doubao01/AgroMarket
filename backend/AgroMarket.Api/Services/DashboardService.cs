using System.Data;
using AgroMarket.Api.Common;
using Dapper;
using Microsoft.Extensions.Caching.Memory;

namespace AgroMarket.Api.Services;

/// <summary>数据大屏统计服务。真实成交口径与 AdminService 保持一致 (排除演示单与 MOCK 支付)。统计结果做 30s 内存短缓存。</summary>
public class DashboardService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IDbConnectionFactory _db;
    private readonly IMemoryCache _cache;

    public DashboardService(IDbConnectionFactory db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private static async Task EnsureAdminAsync(IDbConnection conn, long userId)
    {
        var isAdmin = await conn.ExecuteScalarAsync<bool>("SELECT role='ADMIN' FROM sys_user WHERE id=@id", new { id = userId });
        if (!isAdmin) throw new BizException("需要管理员权限", ErrorCodes.Forbidden);
    }

    /// <summary>管理员校验通过后再查缓存, 保证越权不被缓存绕过。</summary>
    private async Task<T> CachedAsync<T>(string key, Func<Task<T>> factory)
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null) return cached;
        var value = await factory();
        _cache.Set(key, value, CacheTtl);
        return value;
    }

    // 真实成交订单的公共谓词 (联动模块复用同一口径)
    public const string RealOrderWhere =
        @"o.status='COMPLETED' AND o.is_demo=0 AND EXISTS (
            SELECT 1 FROM payment_record p
            WHERE p.order_id=o.id AND p.status='SUCCESS' AND p.channel NOT IN ('MOCK','OFFLINE','DEMO'))";

    public async Task<Dtos.DashboardOverviewDto> GetOverviewAsync(long operatorId)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);

        return await CachedAsync("dash:overview", async () =>
        {
            var users = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sys_user");
            var activeUsers = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sys_user WHERE last_login_at > DATE_SUB(NOW(3), INTERVAL 30 DAY)");
            var supplies = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM product_supply WHERE status='ACTIVE'");
            var demands = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM purchase_demand WHERE status='ACTIVE'");

            var orders = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(DISTINCT o.id) FROM trade_order o WHERE {RealOrderWhere}");
            var amount = await conn.ExecuteScalarAsync<decimal>(
                $"SELECT COALESCE(SUM(o.total_amount),0) FROM trade_order o WHERE {RealOrderWhere}");

            var todayOrders = await conn.ExecuteScalarAsync<long>(
                $"SELECT COUNT(DISTINCT o.id) FROM trade_order o WHERE {RealOrderWhere} AND o.completed_at >= CURDATE()");
            var todayAmount = await conn.ExecuteScalarAsync<decimal>(
                $"SELECT COALESCE(SUM(o.total_amount),0) FROM trade_order o WHERE {RealOrderWhere} AND o.completed_at >= CURDATE()");

            // 注: 周/演示 统计当前前端未渲染, 保留字段以兼容既有接口契约 (改动前请确认无其他消费方)。
            var weekOrders = await conn.ExecuteScalarAsync<long>(
                $"SELECT COUNT(DISTINCT o.id) FROM trade_order o WHERE {RealOrderWhere} AND o.completed_at >= DATE_SUB(CURDATE(), INTERVAL 6 DAY)");
            var weekAmount = await conn.ExecuteScalarAsync<decimal>(
                $"SELECT COALESCE(SUM(o.total_amount),0) FROM trade_order o WHERE {RealOrderWhere} AND o.completed_at >= DATE_SUB(CURDATE(), INTERVAL 6 DAY)");

            var demoOrders = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM trade_order WHERE status='COMPLETED' AND is_demo=1");
            var demoAmount = await conn.ExecuteScalarAsync<decimal>(
                "SELECT COALESCE(SUM(total_amount),0) FROM trade_order WHERE status='COMPLETED' AND is_demo=1");

            return new Dtos.DashboardOverviewDto(users, activeUsers, supplies, demands,
                orders, amount, todayOrders, todayAmount, weekOrders, weekAmount, demoOrders, demoAmount);
        });
    }

    /// <summary>成交趋势 (按天, 最近 N 天, range=7d/30d/90d)。</summary>
    public async Task<List<Dtos.TrendPointDto>> GetTrendAsync(long operatorId, string? range)
    {
        var days = range switch { "7d" => 7, "90d" => 90, _ => 30 };
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);

        return await CachedAsync($"dash:trend:{days}", async () =>
        {
            // 生成日期序列左连接成交数据, 补齐无成交的日期。
            // DATE(o.completed_at) 使 completed_at 索引失效(non-sargable), 但趋势查询结果被缓存, 数据集小, 可接受。
            var rows = await conn.QueryAsync<(string Date, long OrderCount, decimal DealAmount)>(
                $@"WITH RECURSIVE d AS (
                       SELECT CURDATE() - INTERVAL @d-1 DAY AS dt
                       UNION ALL SELECT dt + INTERVAL 1 DAY FROM d WHERE dt < CURDATE()
                   )
                   SELECT DATE_FORMAT(d.dt,'%Y-%m-%d') Date,
                          COUNT(DISTINCT o.id) OrderCount,
                          COALESCE(SUM(o.total_amount),0) DealAmount
                   FROM d
                   LEFT JOIN trade_order o ON DATE(o.completed_at)=d.dt AND {RealOrderWhere}
                   GROUP BY d.dt ORDER BY d.dt",
                new { d = days });
            return rows.Select(r => new Dtos.TrendPointDto(r.Date, r.OrderCount, r.DealAmount)).ToList();
        });
    }

    /// <summary>品类分布 (供应/需求数量 + 真实成交额)。</summary>
    public async Task<List<Dtos.CategoryStatDto>> GetCategoryAsync(long operatorId)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        return await CachedAsync("dash:category", async () =>
        {
            // parent_id IS NOT NULL 即排除顶级(分组)分类, 只统计末级可发布品类。
            var rows = await conn.QueryAsync<(long CategoryId, string CategoryName, long SupplyCount, long DemandCount, decimal DealAmount)>(
                @"SELECT c.id CategoryId, c.name CategoryName,
                         (SELECT COUNT(*) FROM product_supply s WHERE s.category_id=c.id AND s.status='ACTIVE') SupplyCount,
                         (SELECT COUNT(*) FROM purchase_demand d WHERE d.category_id=c.id AND d.status='ACTIVE') DemandCount,
                         COALESCE((
                            SELECT SUM(i.amount) FROM trade_order_item i
                            JOIN product_supply ps ON ps.id=i.supply_id
                            JOIN trade_order o ON o.id=i.order_id
                            WHERE ps.category_id=c.id AND " + RealOrderWhere + @"), 0) DealAmount
                  FROM product_category c
                  WHERE c.is_enabled=1 AND c.parent_id IS NOT NULL
                  ORDER BY SupplyCount DESC, c.sort_order");
            return rows.Select(r => new Dtos.CategoryStatDto(r.CategoryName, r.SupplyCount, r.DemandCount, r.DealAmount)).ToList();
        });
    }

    /// <summary>地区分布 (息县下辖乡镇: 供需数量 + 真实成交额)。</summary>
    public async Task<List<Dtos.RegionStatDto>> GetRegionAsync(long operatorId)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        return await CachedAsync("dash:region", async () =>
        {
            // 注意: parent_id=3 为"息县"的固定 ID (历史约定, 迁移后如改变需同步)。
            // DealAmount 经 quote→supply 定位乡镇, 与 GetOverviewAsync 按订单归属的成交口径存在差异, 属已知口径偏差。
            var rows = await conn.QueryAsync<(long TownId, string TownName, long SupplyCount, long DemandCount, decimal DealAmount)>(
                @"SELECT r.id TownId, r.region_name TownName,
                         (SELECT COUNT(*) FROM product_supply s WHERE s.town_id=r.id AND s.status='ACTIVE') SupplyCount,
                         (SELECT COUNT(*) FROM purchase_demand d WHERE d.town_id=r.id AND d.status='ACTIVE') DemandCount,
                         COALESCE((
                            SELECT SUM(o.total_amount) FROM trade_order o
                            JOIN trade_quote q ON q.id=o.quote_id
                            JOIN product_supply ps ON ps.id=q.supply_id
                            WHERE ps.town_id=r.id AND " + RealOrderWhere + @"), 0) DealAmount
                  FROM sys_region r
                  WHERE r.region_level=4 AND r.parent_id=3 AND r.is_enabled=1
                  ORDER BY r.sort_order");
            return rows.Select(r => new Dtos.RegionStatDto(r.TownId, r.TownName, r.SupplyCount, r.DemandCount, r.DealAmount)).ToList();
        });
    }

    /// <summary>用户结构 (按角色计数)。</summary>
    public async Task<List<Dtos.RoleStatDto>> GetUsersAsync(long operatorId)
    {
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        return await CachedAsync("dash:users", async () =>
        {
            var rows = await conn.QueryAsync<(string Role, long Count)>(
                "SELECT role Role, COUNT(*) Count FROM sys_user GROUP BY role ORDER BY Count DESC");
            return rows.Select(r => new Dtos.RoleStatDto(r.Role, r.Count)).ToList();
        });
    }

    /// <summary>用户趋势 (按日新增与当日活跃)。</summary>
    public async Task<List<Dtos.UserTrendPointDto>> GetUsersTrendAsync(long operatorId, string? range)
    {
        var days = range switch { "7d" => 7, "90d" => 90, _ => 30 };
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        return await CachedAsync($"dash:usertrend:{days}", async () =>
        {
            var rows = await conn.QueryAsync<(string Date, long NewUsers, long ActiveUsers)>(
                @"WITH RECURSIVE d AS (
                       SELECT CURDATE() - INTERVAL @d-1 DAY AS dt
                       UNION ALL SELECT dt + INTERVAL 1 DAY FROM d WHERE dt < CURDATE()
                   )
                   SELECT DATE_FORMAT(d.dt,'%Y-%m-%d') Date,
                          COUNT(u.id) NewUsers,
                          COUNT(u2.id) ActiveUsers
                   FROM d
                   LEFT JOIN sys_user u ON DATE(u.created_at)=d.dt
                   LEFT JOIN sys_user u2 ON DATE(u2.last_login_at)=d.dt
                   GROUP BY d.dt ORDER BY d.dt",
                new { d = days });
            return rows.Select(r => new Dtos.UserTrendPointDto(r.Date, r.NewUsers, r.ActiveUsers)).ToList();
        });
    }

    /// <summary>消息活跃 (最近 N 天消息量与会话数)。</summary>
    public async Task<List<Dtos.ChatActivityDto>> GetChatActivityAsync(long operatorId, string? range)
    {
        var days = range switch { "7d" => 7, "90d" => 90, _ => 30 };
        using var conn = _db.Create();
        await EnsureAdminAsync(conn, operatorId);
        return await CachedAsync($"dash:chat:{days}", async () =>
        {
            var rows = await conn.QueryAsync<(string Date, long MessageCount, long ConversationCount)>(
                @"WITH RECURSIVE d AS (
                       SELECT CURDATE() - INTERVAL @d-1 DAY AS dt
                       UNION ALL SELECT dt + INTERVAL 1 DAY FROM d WHERE dt < CURDATE()
                   )
                   SELECT DATE_FORMAT(d.dt,'%Y-%m-%d') Date,
                          COUNT(m.id) MessageCount,
                          COUNT(DISTINCT m.conversation_id) ConversationCount
                   FROM d
                   LEFT JOIN chat_message m ON DATE(m.created_at)=d.dt
                   GROUP BY d.dt ORDER BY d.dt",
                new { d = days });
            return rows.Select(r => new Dtos.ChatActivityDto(r.Date, r.MessageCount, r.ConversationCount)).ToList();
        });
    }
}
