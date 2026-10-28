using System.Diagnostics;
using AgroMarket.Api.Data;
using AgroMarket.Api.Services;
using Dapper;

namespace AgroMarket.Api.Notifications;

/// <summary>
/// 订单超时关闭任务。
///
/// 背景: 下单即扣库存(TradeService.CreateOrderFromQuoteAsync), 但此前系统没有任何自动关闭机制 ——
/// 买方弃单、卖方久不确认的订单会永久占着库存, 商品显示"售罄"却无人买, 且没有人工释放接口。
///
/// 规则:
///  - PENDING_CONFIRM 超过 confirmHours(默认 48h) 未由卖方确认 -> 关闭
///  - TO_PAY 超过 payHours(默认 24h) 未支付 -> 关闭
/// 两类均调用 TradeService.CancelExpiredAsync: 它在行锁内重读状态并二次校验"未支付",
/// 因此与用户手动确认/支付并发时不会误关。
/// </summary>
public class OrderTimeoutWorker : BackgroundService
{
    private const string LockName = "agromarket_order_timeout";

    private readonly IDbConnectionFactory _db;
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _cfg;
    private readonly ILogger<OrderTimeoutWorker> _logger;

    // TradeService 是 scoped, 后台任务不能直接注入 scoped 服务, 每轮自建 scope
    public OrderTimeoutWorker(IDbConnectionFactory db, IServiceScopeFactory scopes, IConfiguration cfg, ILogger<OrderTimeoutWorker> logger)
    { _db = db; _scopes = scopes; _cfg = cfg; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 错峰启动, 避免多实例在整点同时开跑
        await Task.Delay(Random.Shared.Next(60, 121) * 1000, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "order timeout round failed");
            }
            await Task.Delay(IntervalMinutes() * 60 * 1000, stoppingToken);
        }
    }

    private int IntervalMinutes()
    {
        var v = _cfg.GetValue<int?>("Order:TimeoutScanMinutes") ?? 10;
        return Math.Max(1, v);
    }

    private int ConfirmHours() => Math.Max(1, _cfg.GetValue<int?>("Order:ConfirmTimeoutHours") ?? 48);
    private int PayHours() => Math.Max(1, _cfg.GetValue<int?>("Order:PayTimeoutHours") ?? 24);

    /// <summary>执行一轮超时关闭。Busy=true 表示命名锁被占用(另一个实例或后台循环正在跑), 此时 Closed 无意义。</summary>
    public async Task<(int Closed, bool Busy)> RunOnceAsync(CancellationToken ct = default)
    {
        using var conn = _db.Create();
        // GET_LOCK 是会话级的, 必须显式 Open 保证加锁/查询/解锁落在同一个物理连接上
        // (Dapper 在连接 Closed 时会自己开一条、用完归还连接池, 锁会挂到闲置会话里)
        conn.Open();

        var locked = await conn.ExecuteScalarAsync<int?>($"SELECT GET_LOCK('{LockName}', 0)") == 1;
        if (!locked) return (0, true);

        try
        {
            using var scope = _scopes.CreateScope();
            var trade = scope.ServiceProvider.GetRequiredService<TradeService>();
            var sw = Stopwatch.StartNew();
            // 只取 id, 具体关闭在 CancelExpiredAsync 内用自己的事务与行锁完成。
            // 演示数据(is_demo=1)不参与自动关闭: 否则演示库里的样板订单会在 48h 后被
            // 后台任务悄悄改成 CANCELLED 并回补库存, 演示环境过一夜就"自己变了"。
            var confirmIds = await conn.QueryAsync<long>(
                @"SELECT id FROM trade_order
                  WHERE status='PENDING_CONFIRM' AND is_demo=0
                    AND created_at < DATE_SUB(NOW(3), INTERVAL @h HOUR)
                  ORDER BY id LIMIT 200", new { h = ConfirmHours() });

            // 待付款的计时基准是"进入 TO_PAY"的时间而非下单时间(卖方可能隔天才确认),
            // 与报警扫描的 ORDER_PAY_TIMEOUT 口径保持一致
            var payIds = await conn.QueryAsync<long>(
                @"SELECT o.id FROM trade_order o
                  LEFT JOIN (
                      SELECT order_id, MAX(created_at) AS to_pay_at
                      FROM trade_order_status_log WHERE to_status='TO_PAY' GROUP BY order_id
                  ) l ON l.order_id = o.id
                  WHERE o.status='TO_PAY' AND o.is_demo=0
                    AND COALESCE(l.to_pay_at, o.created_at) < DATE_SUB(NOW(3), INTERVAL @h HOUR)
                  ORDER BY o.id LIMIT 200", new { h = PayHours() });

            var closed = 0;
            foreach (var id in confirmIds)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (await trade.CancelExpiredAsync(id, $"超过 {ConfirmHours()} 小时未确认, 系统自动关闭"))
                        closed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "close expired order {OrderId} failed", id);
                }
            }
            foreach (var id in payIds)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (await trade.CancelExpiredAsync(id, $"超过 {PayHours()} 小时未支付, 系统自动关闭"))
                        closed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "close expired order {OrderId} failed", id);
                }
            }

            // 顺带把已过期的挂牌置为 EXPIRED: expire_at 此前只用于发报警, 没有任何任务改状态,
            // 导致过期挂牌仍可按 status=ACTIVE 被报价/下单。demand 同构。
            var expired = await conn.ExecuteAsync(
                "UPDATE product_supply SET status='EXPIRED' WHERE status='ACTIVE' AND expire_at IS NOT NULL AND expire_at < NOW(3)")
                + await conn.ExecuteAsync(
                "UPDATE purchase_demand SET status='EXPIRED' WHERE status='ACTIVE' AND expire_at IS NOT NULL AND expire_at < NOW(3)");
            if (expired > 0) _logger.LogInformation("listing auto-expired count={Count}", expired);
            // 已接受但未下单的报价过期后置 CLOSED, 释放其占用的可售量(基于 reserved 计算口径)
            var closedQuotes = await conn.ExecuteAsync(
                "UPDATE trade_quote SET status='CLOSED' WHERE status='ACCEPTED' AND valid_until IS NOT NULL AND valid_until < NOW(3)");
            if (closedQuotes > 0) _logger.LogInformation("quote auto-closed count={Count}", closedQuotes);

            if (closed > 0)
                _logger.LogInformation("order timeout closed={Closed} elapsed={Elapsed}ms", closed, sw.ElapsedMilliseconds);
            return (closed, false);
        }
        finally
        {
            try { await conn.ExecuteScalarAsync<int?>($"SELECT RELEASE_LOCK('{LockName}')"); }
            catch (Exception ex) { _logger.LogWarning(ex, "release order timeout lock failed; lock freed when session ends"); }
        }
    }
}
