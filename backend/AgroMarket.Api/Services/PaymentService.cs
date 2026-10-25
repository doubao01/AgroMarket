using AgroMarket.Api.Common;
using AgroMarket.Api.Dtos;
using AgroMarket.Api.Notifications;
using AgroMarket.Api.Services.Payments;
using Dapper;

namespace AgroMarket.Api.Services;

/// <summary>
/// 支付编排: 校验订单/买家 -> 选择渠道 -> 记录 payment_record -> 成功后置订单为 TO_SHIP。
/// 渠道不可用时直接拒绝(501), 不写任何支付记录, 不伪造支付结果。
/// </summary>
public class PaymentService
{
    private readonly IDbConnectionFactory _db;
    private readonly IReadOnlyDictionary<string, IPaymentProvider> _providers;
    private readonly MessageService _messages;
    private readonly AlertService _alerts;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(IDbConnectionFactory db, IEnumerable<IPaymentProvider> providers,
        MessageService messages, AlertService alerts, ILogger<PaymentService> logger)
    {
        _db = db;
        _messages = messages;
        _alerts = alerts;
        _logger = logger;
        _providers = providers.ToDictionary(p => p.Channel, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>可用支付渠道列表(含是否已配置)。</summary>
    public IReadOnlyList<PaymentChannelDto> ListChannels()
        => _providers.Values.OrderBy(p => p.Channel)
            .Select(p => new PaymentChannelDto(p.Channel, ChannelName(p.Channel), p.IsAvailable, p.Channel == "MOCK"))
            .ToList();

    public async Task<PaymentDto> CreateAsync(long userId, PaymentCreateRequest req, CancellationToken ct = default)
    {
        var channel = (req.Channel ?? "MOCK").Trim().ToUpperInvariant();
        if (!_providers.TryGetValue(channel, out var provider))
            throw new BizException($"不支持的支付渠道 [{channel}]", ErrorCodes.BadRequest);
        if (!provider.IsAvailable)
            throw new BizException($"支付渠道 [{channel}] 未配置或未启用", ErrorCodes.NotImplemented);

        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var o = await conn.QueryFirstOrDefaultAsync<(long Id, long BuyerId, long SellerId, string OrderNo,
                decimal TotalAmount, string Status, string? PayChannel)>(
                "SELECT id, buyer_id, seller_id, order_no, total_amount, status, pay_channel FROM trade_order WHERE id=@id FOR UPDATE",
                new { id = req.OrderId }, tx);
            if (o == default) throw new BizException("订单不存在", ErrorCodes.NotFound);
            if (o.BuyerId != userId) throw new BizException("只有买方可以发起支付", ErrorCodes.Forbidden);
            if (o.Status != "TO_PAY") throw new BizException($"订单状态 [{o.Status}] 不可支付", ErrorCodes.Conflict);

            // 不变量: 同一订单同一渠道在任一时刻至多一条待支付记录(与 token 无关)。
            // 早期版本只在"没带 token"时才复用 INIT, 换一个新 token 就能再插一条 —— 真实
            // 渠道下每张 INIT 都是独立可支付凭证, 同一笔订单会被收两次钱, 且回调按 id 只认
            // 最早那张, 真实交易号会记到旧单上。因此先无条件找 INIT, 没有再按 token 找历史
            // 记录(可能已支付)。
            var token = string.IsNullOrWhiteSpace(req.ClientToken) ? null : req.ClientToken.Trim();
            if (token is { Length: > 64 })
                throw new BizException("clientToken 过长, 最多 64 字符", ErrorCodes.BadRequest);

            var existing = await conn.QueryFirstOrDefaultAsync<(long Id, string Status, string? TradeNo, string? PayUrl)>(
                @"SELECT id, status, trade_no, pay_url FROM payment_record
                  WHERE order_id=@oid AND channel=@ch
                    AND (status='INIT' OR (@tk IS NOT NULL AND client_token=@tk))
                  ORDER BY (status='INIT') DESC, id DESC LIMIT 1 FOR UPDATE",
                new { oid = o.Id, ch = channel, tk = token }, tx);

            PaymentInitResult result;
            long payId;
            if (existing.Id != 0)
            {
                // 命中已有记录: 未支付则复用(不再向渠道重复下单), 已支付则直接返回原单。
                // pay_url 必须一并回传, 否则用户关页重进后拿到 null, 无码可扫、这笔钱付不出去。
                result = new PaymentInitResult(existing.Status == "SUCCESS", existing.TradeNo, existing.PayUrl);
                payId = existing.Id;
            }
            else
            {
                result = await provider.CreateAsync(o.Id, o.OrderNo, o.TotalAmount, ct);
                payId = await conn.ExecuteScalarAsync<long>(@"
                    INSERT INTO payment_record (order_id, channel, client_token, trade_no, pay_url, amount, status, callback_raw)
                    VALUES (@oid, @ch, @tk, @tn, @pu, @amt, @st, NULL); SELECT LAST_INSERT_ID();",
                    new { oid = o.Id, ch = channel, tk = token, tn = result.TradeNo, pu = result.PayUrl,
                          amt = o.TotalAmount, st = result.Success ? "SUCCESS" : "INIT" }, tx);
            }

            // 用带状态条件的 UPDATE 判定"是否由本次调用真正推进了订单": 受影响行数为 1 才写状态日志。
            // 这样无论是命中已支付的旧记录, 还是并发下另一个请求抢先置了 TO_SHIP, 都不会重复记一条流转。
            var advanced = 0;
            if (result.Success)
                advanced = await conn.ExecuteAsync(
                    "UPDATE trade_order SET status='TO_SHIP', pay_channel=@ch, paid_at=NOW(3) WHERE id=@id AND status='TO_PAY'",
                    new { id = o.Id, ch = channel }, tx);
            if (advanced == 1)
                await conn.ExecuteAsync(
                    "INSERT INTO trade_order_status_log (order_id, from_status, to_status, operator_id, note) VALUES (@oid,'TO_PAY','TO_SHIP',@uid,@note)",
                    new { oid = o.Id, uid = userId, note = $"支付成功({channel})" }, tx);
            tx.Commit();

            if (advanced == 1)
                await _messages.SendAsync(o.SellerId, userId, "ORDER", "订单已支付", $"订单 #{o.Id} 已支付, 请及时发货", o.Id);

            var created = await conn.QueryFirstAsync<(long Id, long OrderId, string Channel, string? TradeNo, decimal Amount, string Status, DateTime CreatedAt)>(
                "SELECT id, order_id, channel, trade_no, amount, status, created_at FROM payment_record WHERE id=@id",
                new { id = payId });
            return new PaymentDto
            {
                Id = created.Id, OrderId = created.OrderId, Channel = created.Channel, TradeNo = created.TradeNo,
                Amount = created.Amount, Status = created.Status, PaidNow = result.Success,
                PayUrl = result.PayUrl, CreatedAt = created.CreatedAt,
            };
        }
        catch
        {
            if (tx.Connection != null) tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 处理渠道异步回调: 校验并解析 -> 幂等地将订单由 TO_PAY 置为 TO_SHIP。
    /// 返回 true 表示已确认收款(或订单此前已支付); 渠道未启用抛 501, 验签失败抛 400。
    /// </summary>
    public async Task<bool> HandleNotifyAsync(string channel, PaymentNotifyContext ctx, CancellationToken ct = default)
    {
        var key = (channel ?? "").Trim().ToUpperInvariant();
        if (!_providers.TryGetValue(key, out var provider) || !provider.IsAvailable)
            throw new BizException($"支付渠道 [{key}] 未启用", ErrorCodes.NotImplemented);

        var result = await provider.ParseNotifyAsync(ctx, ct);
        if (result == null || !result.Success || string.IsNullOrWhiteSpace(result.OrderNo))
            return false;

        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var o = await conn.QueryFirstOrDefaultAsync<(long Id, long BuyerId, long SellerId, decimal TotalAmount, string Status)>(
                "SELECT id, buyer_id, seller_id, total_amount, status FROM trade_order WHERE order_no=@no FOR UPDATE",
                new { no = result.OrderNo }, tx);
            if (o == default) { tx.Commit(); return false; }

            // 幂等: 订单已离开 TO_PAY(已支付/已发货/已完成)视为回调成功, 不重复处理
            if (o.Status != "TO_PAY")
            {
                if (o.Status == "CANCELLED")
                {
                    // 钱收了但单已取消: 必须留下退款标记, 否则资损且无人知晓。
                    var firstTime = await MarkRefundRequiredAsync(conn, tx, o.Id, key, result.TradeNo, result.RawPayload);
                    tx.Commit();
                    // 只在首次标记时告警: 渠道会重试回调, 每次都通知会把管理员消息刷爆
                    if (firstTime)
                    {
                        _logger.LogWarning("payment received for cancelled order {OrderId} via {Channel}, refund flag set", o.Id, key);
                        await _alerts.NotifyAdminsAsync("退款待处理",
                            $"订单 #{o.Id} 已取消但收到 {key} 渠道付款 {o.TotalAmount} 元, 已标记待退款", o.Id);
                    }
                    // 回 ACK: 钱确实收到了, 回 false 会让渠道无限重试同一笔回调
                    return true;
                }
                tx.Commit();
                return true;
            }

            // 只更新"这一笔": 优先按渠道交易号精确匹配, 否则取最早一条 INIT 记录。
            // 早期实现按 (order_id, channel) 批量更新, 会把该订单所有历史记录一起刷成
            // SUCCESS 并覆盖 trade_no, 真实的交易号被最后一条冲掉。
            var target = await conn.QueryFirstOrDefaultAsync<long?>(
                result.TradeNo == null
                    ? "SELECT id FROM payment_record WHERE order_id=@oid AND channel=@ch AND status='INIT' ORDER BY id LIMIT 1 FOR UPDATE"
                    : "SELECT id FROM payment_record WHERE order_id=@oid AND channel=@ch AND (trade_no=@tn OR status='INIT') ORDER BY (trade_no=@tn) DESC, id LIMIT 1 FOR UPDATE",
                new { oid = o.Id, ch = key, tn = result.TradeNo }, tx);
            if (target == null)
                await conn.ExecuteAsync(
                    "INSERT INTO payment_record (order_id, channel, trade_no, amount, status, callback_raw) VALUES (@oid, @ch, @tn, @amt, 'SUCCESS', @raw)",
                    new { oid = o.Id, ch = key, tn = result.TradeNo, amt = o.TotalAmount, raw = result.RawPayload }, tx);
            else
                await conn.ExecuteAsync(
                    "UPDATE payment_record SET status='SUCCESS', trade_no=COALESCE(@tn, trade_no), callback_raw=COALESCE(@raw, callback_raw) WHERE id=@id",
                    new { id = target.Value, tn = result.TradeNo, raw = result.RawPayload }, tx);

            await conn.ExecuteAsync(
                "UPDATE trade_order SET status='TO_SHIP', pay_channel=@ch, paid_at=NOW(3) WHERE id=@id",
                new { id = o.Id, ch = key }, tx);
            await conn.ExecuteAsync(
                "INSERT INTO trade_order_status_log (order_id, from_status, to_status, operator_id, note) VALUES (@oid,'TO_PAY','TO_SHIP',@uid,@note)",
                new { oid = o.Id, uid = o.BuyerId, note = $"支付成功({key}回调)" }, tx);
            tx.Commit();

            await _messages.SendAsync(o.SellerId, o.BuyerId, "ORDER", "订单已支付", $"订单 #{o.Id} 已支付, 请及时发货", o.Id);
            return true;
        }
        catch
        {
            if (tx.Connection != null) tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 钱已收但订单已被取消(回调晚于取消, 或渠道重复回调): 落一条待退款标记。
    /// 只标记不退款 —— 真实退款必须走渠道接口, 这里保证"有痕迹、可被管理员查到"。
    /// </summary>
    /// <returns>true 表示本次才第一次打上退款标记(用于避免重复告警)</returns>
    private static async Task<bool> MarkRefundRequiredAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx,
        long orderId, string channel, string? tradeNo, string? raw)
    {
        var hit = await conn.ExecuteAsync(@"
            UPDATE payment_record SET status='SUCCESS', trade_no=COALESCE(@tn, trade_no),
                   refund_status='PENDING', callback_raw=COALESCE(@raw, callback_raw)
            WHERE order_id=@oid AND channel=@ch AND status='INIT'
            ORDER BY id LIMIT 1",
            new { oid = orderId, ch = channel, tn = tradeNo, raw = raw }, tx);
        if (hit > 0) return true;

        // 没有 INIT 单说明该渠道此前已记录过一次收款: 已标记退款则不重复插入
        var exists = await conn.ExecuteScalarAsync<int?>(
            "SELECT 1 FROM payment_record WHERE order_id=@oid AND channel=@ch AND status='SUCCESS' AND refund_status='PENDING' LIMIT 1",
            new { oid = orderId, ch = channel }, tx);
        if (exists != null) return false;
        await conn.ExecuteAsync(@"
            INSERT INTO payment_record (order_id, channel, trade_no, amount, status, refund_status, callback_raw)
            SELECT @oid, @ch, @tn, total_amount, 'SUCCESS', 'PENDING', @raw FROM trade_order WHERE id=@oid",
            new { oid = orderId, ch = channel, tn = tradeNo, raw = raw }, tx);
        return true;
    }

    public async Task<List<PaymentDto>> ListByOrderAsync(long userId, long orderId)
    {
        using var conn = _db.Create();
        var access = await conn.QueryFirstOrDefaultAsync<(long BuyerId, long SellerId)>(
            "SELECT buyer_id, seller_id FROM trade_order WHERE id=@id", new { id = orderId });
        if (access == default) throw new BizException("订单不存在", ErrorCodes.NotFound);
        if (userId != access.BuyerId && userId != access.SellerId)
        {
            var isAdmin = await conn.ExecuteScalarAsync<bool>("SELECT role='ADMIN' FROM sys_user WHERE id=@id", new { id = userId });
            if (!isAdmin) throw new BizException("无权查看该订单支付记录", ErrorCodes.Forbidden);
        }
        var rows = await conn.QueryAsync<(long Id, long OrderId, string Channel, string? TradeNo, decimal Amount, string Status, DateTime CreatedAt)>(
            "SELECT id, order_id, channel, trade_no, amount, status, created_at FROM payment_record WHERE order_id=@oid ORDER BY id DESC",
            new { oid = orderId });
        return rows.Select(r => new PaymentDto
        {
            Id = r.Id, OrderId = r.OrderId, Channel = r.Channel, TradeNo = r.TradeNo,
            Amount = r.Amount, Status = r.Status, PaidNow = false, PayUrl = null, CreatedAt = r.CreatedAt,
        }).ToList();
    }

    private static string ChannelName(string ch) => ch switch
    {
        "MOCK" => "开发模拟支付",
        "WECHAT" => "微信支付",
        "ALIPAY" => "支付宝",
        _ => ch,
    };
}