using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgroMarket.Api.Data;
using AgroMarket.Api.Federation.Dtos;
using AgroMarket.Api.Federation.Infrastructure;
using AgroMarket.Api.Services;
using Dapper;
using Microsoft.Extensions.Options;

namespace AgroMarket.Api.Federation.Application;

/// <summary>
/// 本节点数据快照构建器: 从本地业务表提取"汇总指标 + 脱敏业务明细", 供上报/导出使用。
/// 指标口径与数据大屏一致 (真实成交排除演示单与 MOCK 支付); 明细不含任何自然人身份/联系方式。
/// </summary>
public class FederationSnapshotBuilder
{
    private readonly IDbConnectionFactory _db;
    private readonly FederationOptions _opt;

    public FederationSnapshotBuilder(IDbConnectionFactory db, IOptions<FederationOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }

    /// <summary>生成最近 days 天的按天汇总指标。</summary>
    public async Task<List<FedMetricDto>> BuildMetricsAsync(int days)
    {
        if (days < 1) days = 1;
        var from = DateTime.Today.AddDays(-(days - 1));
        using var conn = _db.Create();

        var totalUsers = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sys_user");
        var newUsers = await GroupCount(conn,
            "SELECT DATE(created_at) d, COUNT(*) c FROM sys_user WHERE created_at>=@from GROUP BY d", from);
        var activeUsers = await GroupCount(conn,
            "SELECT DATE(last_login_at) d, COUNT(*) c FROM sys_user WHERE last_login_at>=@from GROUP BY d", from);
        var supplies = await GroupCount(conn,
            "SELECT DATE(created_at) d, COUNT(*) c FROM product_supply WHERE created_at>=@from GROUP BY d", from);
        var demands = await GroupCount(conn,
            "SELECT DATE(created_at) d, COUNT(*) c FROM purchase_demand WHERE created_at>=@from GROUP BY d", from);
        var messages = await GroupCount(conn,
            "SELECT DATE(created_at) d, COUNT(*) c FROM chat_message WHERE created_at>=@from GROUP BY d", from);
        var conversations = await GroupCount(conn,
            "SELECT DATE(created_at) d, COUNT(DISTINCT conversation_id) c FROM chat_message WHERE created_at>=@from GROUP BY d", from);

        var orders = await conn.QueryAsync<(string D, long C, decimal A)>(
            $@"SELECT DATE(o.completed_at) D, COUNT(DISTINCT o.id) C, COALESCE(SUM(o.total_amount),0) A
               FROM trade_order o WHERE {DashboardService.RealOrderWhere} AND o.completed_at>=@from
               GROUP BY DATE(o.completed_at)", new { from });
        var orderMap = orders.ToDictionary(x => x.D, x => (x.C, x.A));

        var list = new List<FedMetricDto>();
        for (var i = 0; i < days; i++)
        {
            var day = from.AddDays(i);
            var key = day.ToString("yyyy-MM-dd");
            var (oc, amt) = orderMap.TryGetValue(key, out var v) ? v : (0L, 0m);
            list.Add(new FedMetricDto
            {
                StatDate = key,
                UserCount = totalUsers,
                ActiveUserCount = activeUsers.GetValueOrDefault(key),
                NewUserCount = newUsers.GetValueOrDefault(key),
                SupplyCount = supplies.GetValueOrDefault(key),
                DemandCount = demands.GetValueOrDefault(key),
                OrderCount = oc,
                TradeAmount = amt,
                MessageCount = messages.GetValueOrDefault(key),
                ConversationCount = conversations.GetValueOrDefault(key),
            });
        }
        return list;
    }

    private static async Task<Dictionary<string, long>> GroupCount(System.Data.IDbConnection conn, string sql, DateTime from)
    {
        var rows = await conn.QueryAsync<(string D, long C)>(sql, new { from });
        return rows.ToDictionary(x => x.D, x => x.C);
    }

    /// <summary>构建时间窗内的脱敏业务明细 (供应/需求/真实成交订单), 各类型不超过 limit 条。</summary>
    public async Task<List<FedRecordDto>> BuildRecordsAsync(DateTime from, DateTime to, int limit)
    {
        using var conn = _db.Create();
        var result = new List<FedRecordDto>();

        // 供应信息
        var supplies = await conn.QueryAsync<(long Id, string Title, string CategoryName, decimal Quantity, string Unit,
            decimal? Price, string PriceUnit, string Status, string? CountyCode, string? CountyName,
            string? TownCode, string? TownName, DateTime CreatedAt)>(
            @"SELECT s.id Id, s.title Title, c.name CategoryName, s.quantity Quantity, s.unit Unit,
                     s.price Price, s.price_unit PriceUnit, s.status Status,
                     cr.region_code CountyCode, cr.region_name CountyName,
                     tr.region_code TownCode, tr.region_name TownName, s.created_at CreatedAt
              FROM product_supply s
              JOIN product_category c ON c.id=s.category_id
              LEFT JOIN sys_region cr ON cr.id=s.county_id
              LEFT JOIN sys_region tr ON tr.id=s.town_id
              WHERE s.created_at BETWEEN @from AND @to AND s.status IN ('ACTIVE','SOLD_OUT','OFF_SHELF')
              ORDER BY s.created_at DESC LIMIT @limit",
            new { from, to, limit });
        foreach (var s in supplies)
            result.Add(NewRecord(FedRecordTypes.Supply, s.Id, s.TownCode ?? s.CountyCode, s.CreatedAt, new
            {
                title = PiiMasker.Mask(s.Title),
                category = s.CategoryName,
                quantity = s.Quantity,
                unit = s.Unit,
                price = s.Price,
                priceUnit = s.PriceUnit,
                status = s.Status,
                countyCode = s.CountyCode,
                countyName = s.CountyName,
                townCode = s.TownCode,
                townName = s.TownName,
            }));

        // 采购需求
        var demands = await conn.QueryAsync<(long Id, string Title, string CategoryName, decimal Quantity, string Unit,
            decimal? PriceMin, decimal? PriceMax, string PriceUnit, string Status,
            string? CountyCode, string? CountyName, string? TownCode, string? TownName, DateTime CreatedAt)>(
            @"SELECT d.id Id, d.title Title, c.name CategoryName, d.quantity Quantity, d.unit Unit,
                     d.price_min PriceMin, d.price_max PriceMax, d.price_unit PriceUnit, d.status Status,
                     cr.region_code CountyCode, cr.region_name CountyName,
                     tr.region_code TownCode, tr.region_name TownName, d.created_at CreatedAt
              FROM purchase_demand d
              JOIN product_category c ON c.id=d.category_id
              LEFT JOIN sys_region cr ON cr.id=d.county_id
              LEFT JOIN sys_region tr ON tr.id=d.town_id
              WHERE d.created_at BETWEEN @from AND @to AND d.status IN ('ACTIVE','CLOSED','OFF_SHELF')
              ORDER BY d.created_at DESC LIMIT @limit",
            new { from, to, limit });
        foreach (var d in demands)
            result.Add(NewRecord(FedRecordTypes.Demand, d.Id, d.TownCode ?? d.CountyCode, d.CreatedAt, new
            {
                title = PiiMasker.Mask(d.Title),
                category = d.CategoryName,
                quantity = d.Quantity,
                unit = d.Unit,
                priceMin = d.PriceMin,
                priceMax = d.PriceMax,
                priceUnit = d.PriceUnit,
                status = d.Status,
                countyCode = d.CountyCode,
                countyName = d.CountyName,
                townCode = d.TownCode,
                townName = d.TownName,
            }));

        // 真实成交订单 (脱敏: 买卖双方仅保留假名, 无联系方式)
        var orders = await conn.QueryAsync<(long Id, decimal TotalAmount, DateTime CompletedAt, long BuyerId, long SellerId,
            string? ProductName, decimal? Quantity, string? Unit, string? CategoryName,
            string? CountyCode, string? CountyName, string? TownCode, string? TownName)>(
            $@"SELECT o.id Id, o.total_amount TotalAmount, o.completed_at CompletedAt,
                      o.buyer_id BuyerId, o.seller_id SellerId,
                      i.product_name ProductName, i.quantity Quantity, i.unit Unit, c.name CategoryName,
                      cr.region_code CountyCode, cr.region_name CountyName,
                      tr.region_code TownCode, tr.region_name TownName
               FROM trade_order o
               LEFT JOIN trade_order_item i ON i.order_id=o.id
               LEFT JOIN product_category c ON c.id=(SELECT category_id FROM product_supply ps WHERE ps.id=i.supply_id)
               LEFT JOIN trade_quote q ON q.id=o.quote_id
               LEFT JOIN product_supply ps2 ON ps2.id=q.supply_id
               LEFT JOIN sys_region cr ON cr.id=ps2.county_id
               LEFT JOIN sys_region tr ON tr.id=ps2.town_id
               WHERE {DashboardService.RealOrderWhere} AND o.completed_at BETWEEN @from AND @to
               ORDER BY o.completed_at DESC LIMIT @limit",
            new { from, to, limit });
        foreach (var o in orders)
            result.Add(NewRecord(FedRecordTypes.Order, o.Id, o.TownCode ?? o.CountyCode, o.CompletedAt, new
            {
                amount = o.TotalAmount,
                productName = PiiMasker.Mask(o.ProductName),
                category = o.CategoryName,
                quantity = o.Quantity,
                unit = o.Unit,
                buyerRef = Pseudonym(o.BuyerId),
                sellerRef = Pseudonym(o.SellerId),
                countyCode = o.CountyCode,
                countyName = o.CountyName,
                townCode = o.TownCode,
                townName = o.TownName,
            }));

        return result;
    }

    private FedRecordDto NewRecord(string type, long id, string? regionCode, DateTime occurredAt, object data) => new()
    {
        Type = type,
        ExternalId = $"{_opt.NodeCode}-{type.ToLowerInvariant()}-{id}",
        RegionCode = regionCode,
        OccurredAt = occurredAt,
        Data = JsonSerializer.SerializeToElement(data),
    };

    /// <summary>把本地用户ID压成不可逆假名 (以本节点编码加盐), 用于跨节点商机撮合但不可还原到人。</summary>
    private string Pseudonym(long userId)
    {
        var raw = Encoding.UTF8.GetBytes($"{_opt.NodeCode}:{userId}:fed");
        var hash = SHA256.HashData(raw);
        return "ref_" + Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
