using AgroMarket.Api.Common;
using AgroMarket.Api.Dtos;
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

    public PaymentService(IDbConnectionFactory db, IEnumerable<IPaymentProvider> providers, MessageService messages)
    {
        _db = db;
        _messages = messages;
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

            var result = await provider.CreateAsync(o.Id, o.OrderNo, o.TotalAmount, ct);
            var status = result.Success ? "SUCCESS" : "INIT";
            var payId = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO payment_record (order_id, channel, trade_no, amount, status, callback_raw)
                VALUES (@oid, @ch, @tn, @amt, @st, NULL); SELECT LAST_INSERT_ID();",
                new { oid = o.Id, ch = channel, tn = result.TradeNo, amt = o.TotalAmount, st = status }, tx);

            if (result.Success)
            {
                await conn.ExecuteAsync(
                    "UPDATE trade_order SET status='TO_SHIP', pay_channel=@ch, paid_at=NOW(3) WHERE id=@id",
                    new { id = o.Id, ch = channel }, tx);
                await conn.ExecuteAsync(
                    "INSERT INTO trade_order_status_log (order_id, from_status, to_status, operator_id, note) VALUES (@oid,'TO_PAY','TO_SHIP',@uid,@note)",
                    new { oid = o.Id, uid = userId, note = $"支付成功({channel})" }, tx);
            }
            tx.Commit();

            if (result.Success)
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
            if (o.Status != "TO_PAY") { tx.Commit(); return o.Status != "CANCELLED"; }

            var updated = await conn.ExecuteAsync(
                "UPDATE payment_record SET status='SUCCESS', trade_no=COALESCE(@tn, trade_no) WHERE order_id=@oid AND channel=@ch AND status<>'SUCCESS'",
                new { oid = o.Id, ch = key, tn = result.TradeNo }, tx);
            if (updated == 0)
                await conn.ExecuteAsync(
                    "INSERT INTO payment_record (order_id, channel, trade_no, amount, status, callback_raw) VALUES (@oid, @ch, @tn, @amt, 'SUCCESS', @raw)",
                    new { oid = o.Id, ch = key, tn = result.TradeNo, amt = o.TotalAmount, raw = result.RawPayload }, tx);

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