using System.Text.Json;
using AgroMarket.Api.Ai.Domain;
using AgroMarket.Api.Services;
using Dapper;

namespace AgroMarket.Api.Ai.Tools;

/// <summary>平台总览数据工具。</summary>
public class PlatformStatsTool : IAiTool
{
    private readonly IDbConnectionFactory _db;
    public PlatformStatsTool(IDbConnectionFactory db) => _db = db;

    public ToolDefinition Definition => new(
        "get_platform_stats",
        "获取平台总览数据: 注册用户数、活跃用户数、在售供应数、在售需求数、真实成交订单数与成交额。用户询问平台整体情况时使用。",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    public async Task<string> InvokeAsync(long userId, string argumentsJson, CancellationToken ct)
    {
        using var conn = _db.Create();
        var users = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sys_user");
        var supplies = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM product_supply WHERE status='ACTIVE'");
        var demands = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM purchase_demand WHERE status='ACTIVE'");
        var orders = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM trade_order WHERE status='COMPLETED' AND is_demo=0");
        var amount = await conn.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(total_amount),0) FROM trade_order WHERE status='COMPLETED' AND is_demo=0");
        return JsonSerializer.Serialize(new
        {
            users, supplies, demands, completedOrders = orders, completedAmount = amount,
        });
    }
}

/// <summary>成交趋势工具。</summary>
public class TradeTrendTool : IAiTool
{
    private readonly IDbConnectionFactory _db;
    public TradeTrendTool(IDbConnectionFactory db) => _db = db;

    public ToolDefinition Definition => new(
        "get_trade_trend",
        "获取最近 N 天的成交趋势(每日成交订单数与成交额)。参数 days 为天数, 默认 30, 最大 90。用户询问近期成交走势时使用。",
        """{"type":"object","properties":{"days":{"type":"integer","description":"天数, 默认30, 最大90"}},"additionalProperties":false}""");

    public async Task<string> InvokeAsync(long userId, string argumentsJson, CancellationToken ct)
    {
        var args = AiToolRegistry.ParseArgs(argumentsJson);
        var days = args.TryGetProperty("days", out var d) && d.TryGetInt32(out var n) ? Math.Clamp(n, 1, 90) : 30;
        using var conn = _db.Create();
        var rows = await conn.QueryAsync<(string Date, long OrderCount, decimal DealAmount)>(
            @"WITH RECURSIVE dd AS (
                  SELECT CURDATE() - INTERVAL @d-1 DAY AS dt
                  UNION ALL SELECT dt + INTERVAL 1 DAY FROM dd WHERE dt < CURDATE()
              )
              SELECT DATE_FORMAT(dd.dt,'%Y-%m-%d') Date,
                     COUNT(DISTINCT o.id) OrderCount,
                     COALESCE(SUM(o.total_amount),0) DealAmount
              FROM dd
              LEFT JOIN trade_order o ON DATE(o.completed_at)=dd.dt AND o.status='COMPLETED' AND o.is_demo=0
              GROUP BY dd.dt ORDER BY dd.dt",
            new { d = days });
        return JsonSerializer.Serialize(new
        {
            days,
            points = rows.Select(r => new { r.Date, r.OrderCount, r.DealAmount }).ToList(),
        });
    }
}

/// <summary>乡镇地区分布工具 (息县下辖乡镇的供需数量与真实成交额, 聚合只读)。</summary>
public class RegionDistributionTool : IAiTool
{
    private readonly IDbConnectionFactory _db;
    public RegionDistributionTool(IDbConnectionFactory db) => _db = db;

    public ToolDefinition Definition => new(
        "get_region_distribution",
        "获取息县各乡镇的农产品供需分布(每个乡镇的在售供应数、在售需求数、真实成交额)。用户询问'哪个乡镇供应/需求最多'或地区分布时使用。",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    public async Task<string> InvokeAsync(long userId, string argumentsJson, CancellationToken ct)
    {
        using var conn = _db.Create();
        var rows = await conn.QueryAsync(
            @"SELECT r.region_name TownName,
                     (SELECT COUNT(*) FROM product_supply s WHERE s.town_id=r.id AND s.status='ACTIVE') SupplyCount,
                     (SELECT COUNT(*) FROM purchase_demand d WHERE d.town_id=r.id AND d.status='ACTIVE') DemandCount,
                     COALESCE((
                        SELECT SUM(o.total_amount) FROM trade_order o
                        JOIN trade_quote q ON q.id=o.quote_id
                        JOIN product_supply ps ON ps.id=q.supply_id
                        WHERE ps.town_id=r.id AND o.status='COMPLETED' AND o.is_demo=0 AND EXISTS (
                            SELECT 1 FROM payment_record p
                            WHERE p.order_id=o.id AND p.status='SUCCESS' AND p.channel NOT IN ('MOCK','OFFLINE','DEMO'))), 0) DealAmount
              FROM sys_region r
              WHERE r.region_level=4 AND r.parent_id=3 AND r.is_enabled=1
              ORDER BY (SupplyCount + DemandCount) DESC, r.sort_order");
        return JsonSerializer.Serialize(new { towns = rows });
    }
}

/// <summary>采购需求搜索工具。</summary>
public class SearchDemandsTool : IAiTool
{
    private readonly IDbConnectionFactory _db;
    public SearchDemandsTool(IDbConnectionFactory db) => _db = db;

    public ToolDefinition Definition => new(
        "search_demands",
        "按关键词搜索在售采购需求。参数 keyword 可选, 匹配标题/描述; limit 可选, 默认 5, 最大 10。用户想了解谁在收购什么农产品时使用。",
        """{"type":"object","properties":{"keyword":{"type":"string"},"limit":{"type":"integer","description":"默认5, 最大10"}},"additionalProperties":false}""");

    public async Task<string> InvokeAsync(long userId, string argumentsJson, CancellationToken ct)
    {
        var args = AiToolRegistry.ParseArgs(argumentsJson);
        var keyword = args.TryGetProperty("keyword", out var k) ? k.GetString() : null;
        var limit = args.TryGetProperty("limit", out var l) && l.TryGetInt32(out var n) ? Math.Clamp(n, 1, 10) : 5;
        using var conn = _db.Create();
        var rows = await conn.QueryAsync(
            @"SELECT title Title, price_min PriceMin, price_max PriceMax, unit Unit, quantity Quantity
              FROM purchase_demand
              WHERE status='ACTIVE' AND (@kw IS NULL OR title LIKE CONCAT('%',@kw,'%'))
              ORDER BY id DESC LIMIT @lim",
            new { kw = string.IsNullOrWhiteSpace(keyword) ? null : keyword, lim = limit });
        return JsonSerializer.Serialize(new { keyword, items = rows });
    }
}

/// <summary>供应商品搜索工具。</summary>
public class SearchSuppliesTool : IAiTool
{
    private readonly IDbConnectionFactory _db;
    public SearchSuppliesTool(IDbConnectionFactory db) => _db = db;

    public ToolDefinition Definition => new(
        "search_supplies",
        "按关键词搜索在售农产品供应。参数 keyword 可选, 匹配标题; limit 可选, 默认 5, 最大 10。用户想了解有哪些农产品在卖及其价格时使用。",
        """{"type":"object","properties":{"keyword":{"type":"string"},"limit":{"type":"integer","description":"默认5, 最大10"}},"additionalProperties":false}""");

    public async Task<string> InvokeAsync(long userId, string argumentsJson, CancellationToken ct)
    {
        var args = AiToolRegistry.ParseArgs(argumentsJson);
        var keyword = args.TryGetProperty("keyword", out var k) ? k.GetString() : null;
        var limit = args.TryGetProperty("limit", out var l) && l.TryGetInt32(out var n) ? Math.Clamp(n, 1, 10) : 5;
        using var conn = _db.Create();
        var rows = await conn.QueryAsync(
            @"SELECT title Title, price Price, price_unit PriceUnit, quantity Quantity, unit Unit
              FROM product_supply
              WHERE status='ACTIVE' AND (@kw IS NULL OR title LIKE CONCAT('%',@kw,'%'))
              ORDER BY id DESC LIMIT @lim",
            new { kw = string.IsNullOrWhiteSpace(keyword) ? null : keyword, lim = limit });
        return JsonSerializer.Serialize(new { keyword, items = rows });
    }
}

/// <summary>当前用户订单概览工具 (仅本人数据)。</summary>
public class MyOrdersTool : IAiTool
{
    private readonly IDbConnectionFactory _db;
    public MyOrdersTool(IDbConnectionFactory db) => _db = db;

    public ToolDefinition Definition => new(
        "get_my_orders",
        "获取当前登录用户自己的订单概览(按状态统计的订单数与金额)。用户询问'我的订单'时使用。",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    public async Task<string> InvokeAsync(long userId, string argumentsJson, CancellationToken ct)
    {
        using var conn = _db.Create();
        var rows = await conn.QueryAsync<(string Status, long Count, decimal Amount)>(
            @"SELECT status Status, COUNT(*) Count, COALESCE(SUM(total_amount),0) Amount
              FROM trade_order WHERE buyer_id=@u OR seller_id=@u GROUP BY status",
            new { u = userId });
        return JsonSerializer.Serialize(new
        {
            asBuyerOrSeller = rows.Select(r => new { r.Status, r.Count, r.Amount }).ToList(),
        });
    }
}
