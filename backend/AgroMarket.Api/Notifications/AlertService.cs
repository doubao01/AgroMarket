using AgroMarket.Api.Common;
using System.Text.Json;
using Dapper;

namespace AgroMarket.Api.Notifications;

/// <summary>报警引擎: 执行启用的规则扫描, 命中写 alert_record (抑制去重) 并通知接收人。</summary>
public class AlertService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>与 DashboardService.RealOrderWhere 一致的真实成交口径。</summary>
    private const string RealOrderWhere =
        @"o.status='COMPLETED' AND o.is_demo=0 AND EXISTS (
            SELECT 1 FROM payment_record p
            WHERE p.order_id=o.id AND p.status='SUCCESS' AND p.channel NOT IN ('MOCK','OFFLINE','DEMO'))";

    public record AlertRule(string Code, bool Enabled, string ThresholdJson, int SuppressHours, string RecipientScope, DateTime UpdatedAt);
    public record AlertRecordDto(long Id, string RuleCode, long ObjectId, string Status, int HitCount, string? Detail, DateTime? ResolvedAt, DateTime CreatedAt);
    public record ScanHit(long ObjectId, string Detail);
    public sealed class ScanSummary
    {
        /// <summary>本轮因为已有一轮在跑而直接跳过(跨进程互斥未取到锁)。</summary>
        public bool Busy { get; set; }
        public int Executed { get; set; }
        public int Hit { get; set; }
        public int Suppressed { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    private readonly IDbConnectionFactory _db;
    private readonly NotificationPushService _push;
    private readonly IConfiguration _cfg;
    private readonly ILogger<AlertService> _logger;

    public AlertService(IDbConnectionFactory db, NotificationPushService push, IConfiguration cfg, ILogger<AlertService> logger)
    { _db = db; _push = push; _cfg = cfg; _logger = logger; }

    // ---------- 规则配置 ----------

    public async Task<List<AlertRule>> GetRulesAsync()
    {
        using var conn = _db.Create();
        return (await conn.QueryAsync<AlertRule>(
            "SELECT code Code, enabled Enabled, threshold_json ThresholdJson, suppress_hours SuppressHours, recipient_scope RecipientScope, updated_at UpdatedAt FROM alert_rule ORDER BY code")).ToList();
    }

    public async Task UpdateRuleAsync(long operatorId, string code, bool? enabled, string? thresholdJson, int? suppressHours, string? recipientScope)
    {
        using var conn = _db.Create();
        var exists = await conn.ExecuteScalarAsync<int?>("SELECT 1 FROM alert_rule WHERE code=@c", new { c = code });
        if (exists == null) throw new BizException("报警规则不存在", ErrorCodes.NotFound);

        if (thresholdJson != null)
        {
            ValidateThreshold(code, thresholdJson);
        }
        if (recipientScope != null && recipientScope is not ("PARTY" or "PARTY_AND_ADMIN" or "ADMIN_ONLY" or "PUBLISHER"))
            throw new BizException("接收人范围取值非法");
        if (suppressHours is < 1 or > 720) throw new BizException("抑制窗口须在 1-720 小时");

        var affected = await conn.ExecuteAsync(@"
            UPDATE alert_rule SET
                enabled = COALESCE(@en, enabled),
                threshold_json = COALESCE(@tj, threshold_json),
                suppress_hours = COALESCE(@sh, suppress_hours),
                recipient_scope = COALESCE(@rs, recipient_scope)
            WHERE code=@c",
            new { c = code, en = enabled, tj = thresholdJson, sh = suppressHours, rs = recipientScope });
        if (affected == 0) throw new BizException("更新失败");
    }

    /// <summary>阈值白名单校验: 每条规则允许的键与取值范围。</summary>
    private static void ValidateThreshold(string code, string thresholdJson)
    {
        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(thresholdJson, Json); }
        catch { throw new BizException("阈值 JSON 格式非法"); }
        switch (code)
        {
            case "ORDER_PAY_TIMEOUT" or "QUOTE_NO_RESPONSE":
                RequireNumber(root, "hours", 1, 720);
                break;
            case "AUDIT_BACKLOG":
                RequireNumber(root, "count", 1, 10000);
                break;
            case "TRADE_ANOMALY":
                RequireNumber(root, "percent", 1, 100);
                break;
            case "LISTING_EXPIRED" or "FED_SYNC_STALE":
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any())
                    throw new BizException("该规则无可配置阈值");
                break;
            default:
                throw new BizException("未知的报警规则");
        }
    }

    private static void RequireNumber(JsonElement root, string key, decimal min, decimal max)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number ||
            v.GetDecimal() < min || v.GetDecimal() > max)
            throw new BizException($"阈值 {key} 须为 {min}-{max} 之间的数值");
    }

    private static T ThresholdOf<T>(string? json, string key, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json, Json);
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var v))
                return (T)Convert.ChangeType(v.GetDecimal(), typeof(T));
        }
        catch { }
        return fallback;
    }

    // ---------- 扫描执行 ----------

    /// <summary>执行一轮扫描。dayRules=false 时跳过日级规则(成交额波动/联动滞后)。通知频次由抑制窗口控制: 同一对象每窗口至多通知一次。</summary>
    public async Task<ScanSummary> ScanAsync(bool dayRules = true)
    {
        var conn = _db.Create();

        // 跨进程互斥: 后台定时器与管理员手动扫描会同时跑同一轮, 造成重复通知与重复落库。
        // 用数据库命名锁(非阻塞)串行化, 锁随连接关闭自动释放。
        var locked = await conn.ExecuteScalarAsync<int?>("SELECT GET_LOCK('agromarket_alert_scan', 0)") == 1;
        if (!locked)
        {
            conn.Dispose();
            return new ScanSummary { Busy = true };
        }

        var rules = (await GetRulesAsync()).Where(r => r.Enabled).ToDictionary(r => r.Code, r => r);
        var summary = new ScanSummary();
        try
        {
            foreach (var (code, rule) in rules)
            {
                // 日级规则(每日一次): dayRules=false 时由调用方跳过
                var isDaily = code is "TRADE_ANOMALY" or "FED_SYNC_STALE";
                if (isDaily && !dayRules) continue;
                try
                {
                    var hits = code switch
                    {
                        "ORDER_PAY_TIMEOUT" => await ScanPayTimeoutAsync(conn, rule),
                        "QUOTE_NO_RESPONSE" => await ScanQuoteNoResponseAsync(conn, rule),
                        "LISTING_EXPIRED" => await ScanListingExpiredAsync(conn),
                        "AUDIT_BACKLOG" => await ScanAuditBacklogAsync(conn, rule),
                        "TRADE_ANOMALY" => await ScanTradeAnomalyAsync(conn, rule),
                        "FED_SYNC_STALE" => await ScanFedStaleAsync(conn),
                        _ => new List<ScanHit>(),
                    };
                    summary.Executed++;
                    foreach (var hit in hits)
                    {
                        var (recorded, suppressed) = await RecordAndNotifyAsync(conn, rule, hit);
                        if (recorded) summary.Hit++; else if (suppressed) summary.Suppressed++;
                    }
                }
                catch (Exception ex)
                {
                    summary.Errors.Add($"{code}: {ex.Message}");
                    _logger.LogWarning(ex, "alert rule {Code} scan failed", code);
                }
            }
            return summary;
        }
        finally
        {
            try { await conn.ExecuteScalarAsync<int?>("SELECT RELEASE_LOCK('agromarket_alert_scan')"); }
            catch { /* 连接已断时锁会随会话释放 */ }
            conn.Dispose();
        }
    }

    /// <summary>
    /// 落 alert_record 并按接收人范围通知。
    /// 唯一键 (rule_code, object_id) 保证每对象仅一条记录:
    /// - 无记录: 插入并通知
    /// - 有记录且已过抑制窗口: 重新打开(更新时间/明细/计数)并再次通知
    /// - 有记录且窗口内: 仅累计 hit_count, 跳过通知
    /// </summary>
    private async Task<(bool recorded, bool suppressed)> RecordAndNotifyAsync(System.Data.IDbConnection conn, AlertRule rule, ScanHit hit)
    {
        var existing = await conn.QueryFirstOrDefaultAsync<(long Id, DateTime CreatedAt, DateTime? ResolvedAt)>(
            "SELECT id, created_at CreatedAt, resolved_at ResolvedAt FROM alert_record WHERE rule_code=@r AND object_id=@o",
            new { r = rule.Code, o = hit.ObjectId });
        if (existing.Id == 0)
        {
            await conn.ExecuteAsync(
                "INSERT INTO alert_record (rule_code, object_id, status, detail) VALUES (@r, @o, 'OPEN', @d)",
                new { r = rule.Code, o = hit.ObjectId, d = hit.Detail });
            await NotifyReceiversAsync(conn, rule, hit);
            return (true, false);
        }
        var windowEnd = existing.ResolvedAt ?? existing.CreatedAt.AddHours(rule.SuppressHours);
        if (existing.ResolvedAt != null || DateTime.Now >= windowEnd)
        {
            // 已解决或超出抑制窗口: 重新打开并通知
            await conn.ExecuteAsync(
                "UPDATE alert_record SET status='OPEN', detail=@d, hit_count=hit_count+1, created_at=NOW(3), resolved_at=NULL WHERE id=@id",
                new { d = hit.Detail, id = existing.Id });
            await NotifyReceiversAsync(conn, rule, hit);
            return (true, false);
        }
        await conn.ExecuteAsync("UPDATE alert_record SET hit_count=hit_count+1 WHERE id=@id", new { id = existing.Id });
        return (false, true);
    }

    private async Task NotifyReceiversAsync(System.Data.IDbConnection conn, AlertRule rule, ScanHit hit)
    {
        var receivers = await ResolveReceiversAsync(conn, rule.RecipientScope, rule.Code, hit.ObjectId);
        foreach (var uid in receivers)
            await _push.NotifyAsync(uid, "ALERT", TitleOf(rule.Code), hit.Detail, hit.ObjectId);
    }

    private async Task<List<long>> ResolveReceiversAsync(System.Data.IDbConnection conn, string scope, string ruleCode, long objectId)
    {
        var ids = new List<long>();
        if (scope is "PARTY" or "PARTY_AND_ADMIN")
        {
            var party = ruleCode switch
            {
                "ORDER_PAY_TIMEOUT" => await conn.QueryAsync<long>(
                    "SELECT buyer_id FROM trade_order WHERE id=@o UNION SELECT seller_id FROM trade_order WHERE id=@o", new { o = objectId }),
                "QUOTE_NO_RESPONSE" => await conn.QueryAsync<long>(
                    "SELECT buyer_id FROM trade_quote WHERE id=@o UNION SELECT seller_id FROM trade_quote WHERE id=@o", new { o = objectId }),
                _ => Enumerable.Empty<long>(),
            };
            ids.AddRange(party);
        }
        if (scope is "ADMIN_ONLY" or "PARTY_AND_ADMIN")
            ids.AddRange(await conn.QueryAsync<long>("SELECT id FROM sys_user WHERE role='ADMIN' AND status='ACTIVE'"));
        if (scope == "PUBLISHER")
        {
            var pub = await conn.ExecuteScalarAsync<long?>(
                "SELECT publisher_id FROM product_supply WHERE id=@o UNION SELECT publisher_id FROM purchase_demand WHERE id=@o", new { o = objectId });
            if (pub != null) ids.Add(pub.Value);
        }
        return ids.Distinct().ToList();
    }

    private static string TitleOf(string code) => code switch
    {
        "ORDER_PAY_TIMEOUT" => "订单支付超时",
        "QUOTE_NO_RESPONSE" => "报价响应超时",
        "LISTING_EXPIRED" => "信息已过期",
        "AUDIT_BACKLOG" => "审核积压",
        "TRADE_ANOMALY" => "成交额异常波动",
        "FED_SYNC_STALE" => "联动同步异常",
        _ => code,
    };

    // ---------- 各规则扫描器 ----------

    private async Task<List<ScanHit>> ScanPayTimeoutAsync(System.Data.IDbConnection conn, AlertRule rule)
    {
        var hours = ThresholdOf(rule.ThresholdJson, "hours", 24);
        // 计时基准必须是"进入待付款"的时间, 不是下单时间: 卖方隔天才确认的订单若按下单时间算,
        // 一进 TO_PAY 就被判超时, 既发误报又提前消耗抑制窗口, 真实超时反而被压掉
        var rows = await conn.QueryAsync<(long Id, string OrderNo, decimal Amount)>(
            @"SELECT o.id, o.order_no OrderNo, o.total_amount Amount
              FROM trade_order o
              LEFT JOIN (
                  SELECT order_id, MAX(created_at) AS to_pay_at
                  FROM trade_order_status_log WHERE to_status='TO_PAY' GROUP BY order_id
              ) l ON l.order_id = o.id
              WHERE o.status='TO_PAY'
                AND COALESCE(l.to_pay_at, o.created_at) < DATE_SUB(NOW(3), INTERVAL @h HOUR)
                AND NOT EXISTS (SELECT 1 FROM alert_record ar
                                WHERE ar.rule_code='ORDER_PAY_TIMEOUT' AND ar.object_id=o.id AND ar.resolved_at IS NULL)
              LIMIT 100",
            new { h = hours });
        return rows.Select(r => new ScanHit(r.Id, $"订单 {r.OrderNo} 已超过 {hours} 小时未支付（金额 {r.Amount} 元），请及时处理")).ToList();
    }

    private async Task<List<ScanHit>> ScanQuoteNoResponseAsync(System.Data.IDbConnection conn, AlertRule rule)
    {
        var hours = ThresholdOf(rule.ThresholdJson, "hours", 48);
        var rows = await conn.QueryAsync<(long Id, string QuoteNo, decimal Price, decimal Qty)>(
            // 已产生未解决报警的对象不再重复计入: 否则每轮扫描都会命中同一批对象,
            // LIMIT 100 被长期占满后, 新出现的超时对象永远轮不到(监控静默失效)
            @"SELECT q.id, q.quote_no QuoteNo, q.price Price, q.quantity Qty
              FROM trade_quote q
              WHERE q.status='PENDING' AND q.created_at < DATE_SUB(NOW(3), INTERVAL @h HOUR)
                AND NOT EXISTS (SELECT 1 FROM alert_record ar
                                WHERE ar.rule_code='QUOTE_NO_RESPONSE' AND ar.object_id=q.id AND ar.resolved_at IS NULL)
              LIMIT 100",
            new { h = hours });
        return rows.Select(r => new ScanHit(r.Id, $"报价 {r.QuoteNo}（{r.Price} 元/单位 x {r.Qty}）已超过 {hours} 小时未响应，请尽快处理")).ToList();
    }

    private async Task<List<ScanHit>> ScanListingExpiredAsync(System.Data.IDbConnection conn)
    {
        var rows = await conn.QueryAsync<(long Id, string Title, string Kind)>(
            @"SELECT s.id, s.title Title, 'SUPPLY' Kind FROM product_supply s
              WHERE s.status='ACTIVE' AND s.expire_at IS NOT NULL AND s.expire_at < NOW(3)
                AND NOT EXISTS (SELECT 1 FROM alert_record ar
                                WHERE ar.rule_code='LISTING_EXPIRED' AND ar.object_id=s.id AND ar.resolved_at IS NULL)
              UNION ALL
              SELECT d.id, d.title, 'DEMAND' FROM purchase_demand d
              WHERE d.status='ACTIVE' AND d.expire_at IS NOT NULL AND d.expire_at < NOW(3)
                AND NOT EXISTS (SELECT 1 FROM alert_record ar
                                WHERE ar.rule_code='LISTING_EXPIRED' AND ar.object_id=d.id AND ar.resolved_at IS NULL)
              LIMIT 100");
        return rows.Select(r => new ScanHit(r.Id, $"您的{(r.Kind == "SUPPLY" ? "供应" : "采购")}「{r.Title}」已过有效期，请及时续期或下架")).ToList();
    }

    private async Task<List<ScanHit>> ScanAuditBacklogAsync(System.Data.IDbConnection conn, AlertRule rule)
    {
        var threshold = ThresholdOf(rule.ThresholdJson, "count", 20);
        var cnt = await conn.ExecuteScalarAsync<long>(
            @"SELECT (SELECT COUNT(*) FROM product_supply WHERE status='PENDING')
                   + (SELECT COUNT(*) FROM purchase_demand WHERE status='PENDING')");
        if (cnt <= threshold) return new List<ScanHit>();
        // 对象固定 0, 抑制频次由 RecordAndNotifyAsync 的窗口控制
        return new List<ScanHit> { new(0, $"当前待审核的供应/采购共 {cnt} 条，已超过阈值 {threshold} 条，请及时处理") };
    }

    private async Task<List<ScanHit>> ScanTradeAnomalyAsync(System.Data.IDbConnection conn, AlertRule rule)
    {
        var percent = ThresholdOf(rule.ThresholdJson, "percent", 50);
        var today = await conn.ExecuteScalarAsync<decimal>(
            $"SELECT COALESCE(SUM(o.total_amount),0) FROM trade_order o WHERE {RealOrderWhere} AND o.completed_at >= CURDATE()");
        var avg7 = await conn.ExecuteScalarAsync<decimal>(
            $@"SELECT COALESCE(SUM(o.total_amount),0) / 7 FROM trade_order o
               WHERE {RealOrderWhere}
                 AND o.completed_at >= DATE_SUB(CURDATE(), INTERVAL 7 DAY) AND o.completed_at < CURDATE()");
        if (avg7 <= 0) return new List<ScanHit>();
        var dev = Math.Abs(today - avg7) / avg7 * 100;
        if (dev <= percent) return new List<ScanHit>();
        var dir = today > avg7 ? "上升" : "下降";
        return new List<ScanHit> { new(0, $"今日真实成交额 {today:F2} 元较前 7 日日均 {avg7:F2} 元{dir} {dev:F0}%（阈值 {percent}%），请关注") };
    }

    private async Task<List<ScanHit>> ScanFedStaleAsync(System.Data.IDbConnection conn)
    {
        var staleMinutes = EffectiveStaleMinutes();
        var rows = await conn.QueryAsync<(long Id, string NodeCode, string NodeName)>(
            @"SELECT n.id, n.node_code NodeCode, n.node_name NodeName
              FROM fed_node n
              WHERE n.status='ACTIVE'
                AND GREATEST(COALESCE(n.last_push_at, n.created_at), COALESCE(n.last_pull_at, n.created_at))
                    < DATE_SUB(NOW(3), INTERVAL @m MINUTE)
                AND NOT EXISTS (SELECT 1 FROM alert_record ar WHERE ar.rule_code='FED_SYNC_STALE' AND ar.object_id=n.id AND ar.resolved_at IS NULL)
              LIMIT 50",
            new { m = staleMinutes });
        var hits = rows.Select(r => new ScanHit(r.Id, $"节点 {r.NodeName}({r.NodeCode}) 已超过 {staleMinutes} 分钟无同步活动，请检查对端服务")).ToList();

        // 恢复通知: 此前 stale 且已 resolved 的节点重新活跃 → 记 RESOLVED 并通知
        var recovered = await conn.QueryAsync<(long Id, string NodeCode, string NodeName)>(
            @"SELECT n.id, n.node_code NodeCode, n.node_name NodeName
              FROM fed_node n
              WHERE n.status='ACTIVE'
                AND EXISTS (SELECT 1 FROM alert_record ar WHERE ar.rule_code='FED_SYNC_STALE' AND ar.object_id=n.id AND ar.resolved_at IS NULL)
                AND GREATEST(COALESCE(n.last_push_at, n.created_at), COALESCE(n.last_pull_at, n.created_at))
                    >= DATE_SUB(NOW(3), INTERVAL @m MINUTE)",
            new { m = staleMinutes });
        foreach (var r in recovered)
        {
            await conn.ExecuteAsync(
                "UPDATE alert_record SET status='RESOLVED', resolved_at=NOW(3) WHERE rule_code='FED_SYNC_STALE' AND object_id=@o AND resolved_at IS NULL",
                new { o = r.Id });
            await _push.NotifyAdminsAsync("SYSTEM", "联动同步恢复", $"节点 {r.NodeName}({r.NodeCode}) 同步已恢复正常", r.Id);
        }
        return hits;
    }

    /// <summary>复用联动健康阈值: HealthStaleMinutes>0 用它, 否则 max(2xSyncIntervalMinutes, 60)。</summary>
    private int EffectiveStaleMinutes()
    {
        var configured = _cfg.GetValue<int?>("Federation:HealthStaleMinutes") ?? 0;
        if (configured > 0) return configured;
        var interval = _cfg.GetValue<int?>("Federation:SyncIntervalMinutes") ?? 30;
        return Math.Max(2 * interval, 60);
    }

    // ---------- 查询 ----------

    public async Task<Dtos.PagedResult<AlertRecordDto>> QueryRecordsAsync(int page, int pageSize, string? ruleCode)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;
        using var conn = _db.Create();
        var where = string.IsNullOrWhiteSpace(ruleCode) ? "1=1" : "rule_code=@rc";
        var total = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM alert_record WHERE {where}",
            new { rc = ruleCode });
        var items = (await conn.QueryAsync<AlertRecordDto>(
            $@"SELECT CAST(id AS SIGNED) Id, rule_code RuleCode, CAST(object_id AS SIGNED) ObjectId,
                      status Status, hit_count HitCount, detail Detail,
                      resolved_at ResolvedAt, created_at CreatedAt
               FROM alert_record WHERE {where} ORDER BY id DESC LIMIT @l OFFSET @o",
            new { rc = ruleCode, l = pageSize, o = Paging.Offset(page, pageSize) })).ToList();
        return new Dtos.PagedResult<AlertRecordDto>(items, total, page, pageSize);
    }
}
