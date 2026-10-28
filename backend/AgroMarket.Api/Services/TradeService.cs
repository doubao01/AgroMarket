using System.Data;
using AgroMarket.Api.Common;
using Dapper;

namespace AgroMarket.Api.Services;

/// <summary>
/// 自由交易核心: 报价 / 还价 / 接受 / 拒绝 / 从报价生成订单 / 订单状态机。
/// 关键操作使用事务 + 行锁防止重复执行; 未经买方确认不会自动生成订单。
/// </summary>
public class TradeService
{
    private readonly IDbConnectionFactory _db;
    private readonly RegionService _regions;
    private readonly MessageService _messages;
    private readonly bool _isDemo;
    private readonly IConfiguration _cfg;

    public TradeService(IDbConnectionFactory db, RegionService regions, MessageService messages, IWebHostEnvironment env, IConfiguration cfg)
    { _db = db; _regions = regions; _messages = messages; _isDemo = env.IsDevelopment(); _cfg = cfg; }

    private static async Task ValidateQuantityAsync(IDbConnection conn, long? supplyId, long? demandId,
        decimal quantity, decimal price, string priceUnit, IDbTransaction? tx = null)
    {
        if (quantity <= 0 || price <= 0 || quantity > 9999999999.99m || price > 9999999999.99m ||
            decimal.Round(quantity, 2) != quantity || decimal.Round(price, 2) != price || price * quantity > 999999999999.99m)
            throw new BizException("价格和数量须为有效正数，最多两位小数，且总金额在允许范围内");
        var sql = supplyId != null
            ? "SELECT quantity, COALESCE(min_order_qty,0), price_unit, status, expire_at FROM product_supply WHERE id=@id"
            : "SELECT quantity, 0, price_unit, status, expire_at FROM purchase_demand WHERE id=@id";
        var listing = await conn.QuerySingleOrDefaultAsync<(decimal Quantity, decimal Min, string PriceUnit, string Status, DateTime? ExpireAt)>(
            sql + (tx != null ? " FOR UPDATE" : ""), new { id = supplyId ?? demandId }, tx);
        if (listing == default || listing.Status != "ACTIVE")
            throw new BizException("信息当前不可交易", ErrorCodes.Conflict);
        // 双保险: 过期挂牌可能尚未被后台置为 EXPIRED, 这里按时间再拦一次
        if (listing.ExpireAt != null && listing.ExpireAt <= DateTime.Now)
            throw new BizException("信息已过期, 不可交易", ErrorCodes.Conflict);
        if (quantity < listing.Min || quantity > listing.Quantity)
            throw new BizException("报价数量须满足起订量且不超过发布数量");
        if (priceUnit != listing.PriceUnit) throw new BizException("报价单位必须与发布信息一致");
    }

    public class QuoteRow
    {
        public long Id { get; set; }
        public string QuoteNo { get; set; } = "";
        public long? SupplyId { get; set; }
        public string? SupplyTitle { get; set; }
        public long? DemandId { get; set; }
        public string? DemandTitle { get; set; }
        public long BuyerId { get; set; }
        public string BuyerName { get; set; } = "";
        public long SellerId { get; set; }
        public string SellerName { get; set; } = "";
        public long InitiatorId { get; set; }
        public decimal Price { get; set; }
        public string PriceUnit { get; set; } = "";
        public decimal Quantity { get; set; }
        public string? Remark { get; set; }
        public string Status { get; set; } = "";
        public long? OrderId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private const string QuoteSelectSql = @"
        SELECT q.id, q.quote_no QuoteNo, q.supply_id SupplyId, s.title SupplyTitle, q.demand_id DemandId, d.title DemandTitle,
               q.buyer_id BuyerId, ub.nickname BuyerName, q.seller_id SellerId, us.nickname SellerName,
               q.initiator_id InitiatorId, q.price, q.price_unit PriceUnit, q.quantity, q.remark, q.status,
               q.order_id OrderId, q.created_at CreatedAt
        FROM trade_quote q
        JOIN sys_user ub ON ub.id = q.buyer_id
        JOIN sys_user us ON us.id = q.seller_id
        LEFT JOIN product_supply s ON s.id = q.supply_id
        LEFT JOIN purchase_demand d ON d.id = q.demand_id
        WHERE 1=1 {0}
        ORDER BY q.created_at DESC, q.id DESC
        LIMIT @limit OFFSET @offset";

    public async Task<Dtos.QuoteDto> CreateQuoteAsync(long userId, Dtos.QuoteCreateRequest req, long? conversationId = null)
    {
        if (req.Price <= 0) throw new BizException("报价单价必须大于 0");
        if (req.Quantity <= 0) throw new BizException("报价数量必须大于 0");
        if (req.SupplyId == null && req.DemandId == null) throw new BizException("必须指定供应商品或采购需求");
        if (req.SupplyId != null && req.DemandId != null) throw new BizException("只能指定供应商品或采购需求其一");

        long buyerId, sellerId;
        string supplyTitle = "", demandTitle = "";
        using (var conn = _db.Create())
        {
            if (req.SupplyId != null)
            {
                var s = await conn.QueryFirstOrDefaultAsync<(long Publisher, string Title, string Status)>(
                    "SELECT publisher_id, title, status FROM product_supply WHERE id=@id", new { id = req.SupplyId });
                if (s == default) throw new BizException("供应商品不存在", ErrorCodes.NotFound);
                if (s.Status != "ACTIVE") throw new BizException("该供应信息当前不可交易");
                if (s.Publisher == userId) throw new BizException("不能对自己的商品报价");
                buyerId = userId; sellerId = s.Publisher; supplyTitle = s.Title;
            }
            else
            {
                var d = await conn.QueryFirstOrDefaultAsync<(long Publisher, string Title, string Status)>(
                    "SELECT publisher_id, title, status FROM purchase_demand WHERE id=@id", new { id = req.DemandId });
                if (d == default) throw new BizException("采购需求不存在", ErrorCodes.NotFound);
                if (d.Status != "ACTIVE") throw new BizException("该采购需求当前不可报价");
                if (d.Publisher == userId) throw new BizException("不能对自己的采购需求报价");
                buyerId = d.Publisher; sellerId = userId; demandTitle = d.Title;
            }
        }

        using var conn2 = _db.Create();
        conn2.Open();
        using var tx = conn2.BeginTransaction();
        try
        {
            await ValidateQuantityAsync(conn2, req.SupplyId, req.DemandId, req.Quantity, req.Price, req.PriceUnit, tx);
            var quoteNo = "Q" + Guid.NewGuid().ToString("N")[..31];
            var id = await conn2.ExecuteScalarAsync<long>(@"
                INSERT INTO trade_quote (quote_no, supply_id, demand_id, buyer_id, seller_id, initiator_id, price, price_unit, quantity, remark, status, conversation_id)
                VALUES (@no, @sid, @did, @b, @s, @init, @price, @punit, @q, @remark, 'PENDING', @conv); SELECT LAST_INSERT_ID();",
                new { no = quoteNo, sid = req.SupplyId, did = req.DemandId, b = buyerId, s = sellerId, init = userId,
                    req.Price, punit = req.PriceUnit, q = req.Quantity, remark = req.Remark, conv = conversationId }, tx);
            tx.Commit();
            var newQuoteId = id;
            await _messages.SendAsync(buyerId == userId ? sellerId : buyerId, userId, "QUOTE",
                "收到新的报价", supplyTitle != "" ? $"您发布的 [{supplyTitle}] 收到新报价: {req.Price} {req.PriceUnit} x {req.Quantity}" : $"您的采购需求 [{demandTitle}] 收到新报价: {req.Price} {req.PriceUnit} x {req.Quantity}",
                newQuoteId);
            return (await GetQuoteByIdAsync(newQuoteId))!;
        }
        catch
        {
            if (tx.Connection != null) tx.Rollback();
            throw;
        }
    }

    public async Task<Dtos.QuoteDto> CounterQuoteAsync(long userId, long quoteId, Dtos.QuoteCounterRequest req)
    {
        if (req.Price <= 0) throw new BizException("还价单价必须大于 0");
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            // 行锁防止并发重复还价
            var q = await conn.QueryFirstOrDefaultAsync<(long Id, long BuyerId, long SellerId, string Status, decimal Quantity, string PriceUnit, long? SupplyId, long? DemandId, long InitiatorId)>(
                "SELECT id, buyer_id, seller_id, status, quantity, price_unit, supply_id, demand_id, initiator_id FROM trade_quote WHERE id=@id FOR UPDATE",
                new { id = quoteId }, tx);
            if (q == default) throw new BizException("报价不存在", ErrorCodes.NotFound);
            if (q.Status != "PENDING")
                throw new BizException("当前报价状态不可还价", ErrorCodes.Conflict);
            if (userId != q.BuyerId && userId != q.SellerId)
                throw new BizException("无权操作该报价", ErrorCodes.Forbidden);
            if (userId == q.InitiatorId) throw new BizException("等待对方回复当前报价", ErrorCodes.Forbidden);

            await conn.ExecuteAsync(
                "UPDATE trade_quote SET status='NEGOTIATED' WHERE id=@id", new { id = quoteId }, tx);
            var newQty = req.Quantity ?? q.Quantity;
            await ValidateQuantityAsync(conn, q.SupplyId, q.DemandId, newQty, req.Price, q.PriceUnit, tx);
            var quoteNo = "Q" + Guid.NewGuid().ToString("N")[..31];
            var newId = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO trade_quote (quote_no, supply_id, demand_id, buyer_id, seller_id, initiator_id, price, price_unit, quantity, remark, status, parent_quote_id)
                VALUES (@no, @sid, @did, @b, @s, @uid, @price, @punit, @q, @remark, 'PENDING', @pid);
                SELECT LAST_INSERT_ID();",
                new { no = quoteNo, sid = q.SupplyId, did = q.DemandId, b = q.BuyerId, s = q.SellerId,
                    uid = userId, req.Price, punit = q.PriceUnit, q = newQty, remark = req.Remark, pid = quoteId }, tx);
            tx.Commit();
            var counterId = newId;
            await _messages.SendAsync(userId == q.BuyerId ? q.SellerId : q.BuyerId, userId, "QUOTE",
                "收到还价", $"您有一条报价被还价: {req.Price} {q.PriceUnit} x {newQty}, 请查看回复", counterId);
            return (await GetQuoteByIdAsync(counterId))!;
        }
        catch
        {
            if (tx.Connection != null) tx.Rollback();
            throw;
        }
    }

    public async Task<Dtos.QuoteDto> AcceptQuoteAsync(long userId, long quoteId)
    {
        // 接受报价仅更新状态, 订单创建须双方确认条款后由买方显式调用
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var q = await conn.QueryFirstOrDefaultAsync<(long Id, long BuyerId, long SellerId, string Status, long? SupplyId, long? DemandId, long InitiatorId, decimal Quantity, decimal Price, string PriceUnit)>(
                "SELECT id, buyer_id, seller_id, status, supply_id, demand_id, initiator_id, quantity, price, price_unit FROM trade_quote WHERE id=@id FOR UPDATE",
                new { id = quoteId }, tx);
            if (q == default) throw new BizException("报价不存在", ErrorCodes.NotFound);
            var isBuyer = userId == q.BuyerId;
            var isSeller = userId == q.SellerId;
            if (!isBuyer && !isSeller) throw new BizException("无权操作该报价", ErrorCodes.Forbidden);
            if (userId == q.InitiatorId) throw new BizException("只有对方可以接受报价", ErrorCodes.Forbidden);
            if (q.Status != "PENDING")
                throw new BizException("该报价已被处理, 请勿重复操作", ErrorCodes.Conflict);
            await ValidateQuantityAsync(conn, q.SupplyId, q.DemandId, q.Quantity, q.Price, q.PriceUnit, tx);
            // 预留校验: 库存在下单时才扣减, 若只看挂牌总量, 同一 supply 可被多张报价同时"接受"满量,
            // 之后只有第一个能下单, 其余永久悬空。这里要求 可售量 - 已接受未成交量 >= 本报价数量。
            // ValidateQuantityAsync 已对挂牌行加了 FOR UPDATE, 并发接受会在此串行, 无竞态。
            if (q.SupplyId != null || q.DemandId != null)
            {
                var tbl = q.SupplyId != null ? "product_supply" : "purchase_demand";
                var col = q.SupplyId != null ? "supply_id" : "demand_id";
                var lid = q.SupplyId ?? q.DemandId;
                var reserved = await conn.ExecuteScalarAsync<decimal>(
                    $"SELECT COALESCE(SUM(quantity),0) FROM trade_quote WHERE status='ACCEPTED' AND {col}=@lid AND id<>@id",
                    new { lid, id = quoteId }, tx);
                var stock = await conn.ExecuteScalarAsync<decimal>($"SELECT quantity FROM {tbl} WHERE id=@lid", new { lid }, tx);
                if (stock - reserved < q.Quantity)
                    throw new BizException("可售数量不足, 已有其他报价占用, 请等待其成交或过期", ErrorCodes.Conflict);
            }
            // valid_until: 接受后 N 小时内未下单则视为失效, 由后台任务置 CLOSED
            var validHours = Math.Max(1, _cfg.GetValue<int?>("Quote:AcceptValidHours") ?? 24);
            await conn.ExecuteAsync(
                "UPDATE trade_quote SET status='ACCEPTED', valid_until=DATE_ADD(NOW(3), INTERVAL @h HOUR) WHERE id=@id",
                new { id = quoteId, h = validHours }, tx);
            tx.Commit();
            var other = isBuyer ? q.SellerId : q.BuyerId;
            await _messages.SendAsync(other, userId, "QUOTE", "报价已被接受", $"报价 #{quoteId} 已被对方接受, 双方可确认条款并创建订单", quoteId);
            return (await GetQuoteByIdAsync(quoteId))!;
        }
        catch
        {
            if (tx.Connection != null) tx.Rollback();
            throw;
        }
    }

    public async Task<Dtos.QuoteDto> RejectQuoteAsync(long userId, long quoteId)
    {
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var q = await conn.QueryFirstOrDefaultAsync<(long Id, long BuyerId, long SellerId, string Status, long InitiatorId)>(
                "SELECT id, buyer_id, seller_id, status, initiator_id FROM trade_quote WHERE id=@id FOR UPDATE",
                new { id = quoteId }, tx);
            if (q == default) throw new BizException("报价不存在", ErrorCodes.NotFound);
            if (userId != q.BuyerId && userId != q.SellerId)
                throw new BizException("无权操作该报价", ErrorCodes.Forbidden);
            if (userId == q.InitiatorId) throw new BizException("只有对方可以拒绝报价", ErrorCodes.Forbidden);
            if (q.Status != "PENDING")
                throw new BizException("该报价已被处理, 请勿重复操作", ErrorCodes.Conflict);
            await conn.ExecuteAsync("UPDATE trade_quote SET status='REJECTED' WHERE id=@id", new { id = quoteId }, tx);
            tx.Commit();
            var other = userId == q.BuyerId ? q.SellerId : q.BuyerId;
            await _messages.SendAsync(other, userId, "QUOTE", "报价被拒绝", $"报价 #{quoteId} 已被对方拒绝", quoteId);
            return (await GetQuoteByIdAsync(quoteId))!;
        }
        catch
        {
            if (tx.Connection != null) tx.Rollback();
            throw;
        }
    }

    /// <summary>由已接受的报价创建订单。只有买方可以确认下单; 已生成订单的报价不可重复下单。</summary>
    public async Task<Dtos.OrderDto> CreateOrderFromQuoteAsync(long buyerId, long quoteId, Dtos.OrderCreateFromQuoteRequest req)
    {
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var q = await conn.QueryFirstOrDefaultAsync<(long Id, long BuyerId, long SellerId, string Status, long? SupplyId, long? DemandId, decimal Price, string PriceUnit, decimal Quantity)>(
                "SELECT id, buyer_id, seller_id, status, supply_id, demand_id, price, price_unit, quantity FROM trade_quote WHERE id=@id FOR UPDATE",
                new { id = quoteId }, tx);
            if (q == default) throw new BizException("报价不存在", ErrorCodes.NotFound);
            if (q.BuyerId != buyerId) throw new BizException("只有买方可以确认下单", ErrorCodes.Forbidden);
            // 幂等须先于状态校验: 重复提交时报价早已 DEALED, 若先查状态会被"尚未被接受"挡掉, 幂等就失效了。
            var clientOrderId = string.IsNullOrWhiteSpace(req.ClientOrderId) ? null : req.ClientOrderId.Trim();
            if (clientOrderId != null)
            {
                var dupId = await conn.ExecuteScalarAsync<long?>(
                    "SELECT id FROM trade_order WHERE buyer_id=@b AND client_order_id=@c LIMIT 1",
                    new { b = buyerId, c = clientOrderId }, tx);
                if (dupId != null) { tx.Commit(); return await GetOrderByIdAsync(dupId.Value, buyerId); }
            }
            if (q.Status != "ACCEPTED") throw new BizException("报价尚未被双方接受, 不能创建订单", ErrorCodes.Conflict);
            // 报价有有效期: 接受后长时间未下单会过期, 由后台置 CLOSED; 这里按时间再拦一次, 避免用已失效报价下单
            var validUntil = await conn.ExecuteScalarAsync<DateTime?>("SELECT valid_until FROM trade_quote WHERE id=@id", new { id = quoteId }, tx);
            if (validUntil != null && validUntil <= DateTime.Now) throw new BizException("该报价已过期, 请重新报价", ErrorCodes.Conflict);
            // 已有订单的报价防重复下单
            var existing = await conn.ExecuteScalarAsync<long?>("SELECT id FROM trade_order WHERE quote_id=@qid LIMIT 1", new { qid = quoteId }, tx);
            if (existing != null) throw new BizException("该报价已生成订单, 请勿重复提交", ErrorCodes.Conflict);

            await ValidateQuantityAsync(conn, q.SupplyId, q.DemandId, q.Quantity, q.Price, q.PriceUnit, tx);

            // 下单即占用库存: 否则同一供应可被反复整量卖出(两次报价都通过校验 -> 超卖)。
            // 用带条件的 UPDATE 做原子扣减, 并发下单时只有一个能成功
            if (q.SupplyId != null)
            {
                var taken = await conn.ExecuteAsync(
                    "UPDATE product_supply SET quantity = quantity - @q WHERE id=@id AND quantity >= @q",
                    new { id = q.SupplyId, q = q.Quantity }, tx);
                if (taken == 0) throw new BizException("可售数量不足, 请刷新后重试", ErrorCodes.Conflict);
                await conn.ExecuteAsync(
                    "UPDATE product_supply SET status='SOLD_OUT' WHERE id=@id AND quantity<=0",
                    new { id = q.SupplyId }, tx);
            }

            // 商品快照来源: 供应报价取商品信息; 采购需求报价取需求信息
            long? snapshotSupplyId = q.SupplyId;
            string productName; string? productSpec; string unit;
            if (q.SupplyId != null)
            {
                var supply = await conn.QueryFirstOrDefaultAsync<(long Id, string Title, string? Spec, string Status, string Unit)>(
                    "SELECT id, title, spec, status, unit FROM product_supply WHERE id=@id", new { id = q.SupplyId }, tx);
                if (supply == default) throw new BizException("供应商品已不存在", ErrorCodes.NotFound);
                productName = supply.Title; productSpec = supply.Spec; unit = supply.Unit;
            }
            else
            {
                var demand = await conn.QueryFirstOrDefaultAsync<(long Id, string Title, string? Spec, string Status, string Unit)>(
                    "SELECT id, title, spec_required Spec, status, unit FROM purchase_demand WHERE id=@id", new { id = q.DemandId }, tx);
                if (demand == default) throw new BizException("采购需求已不存在", ErrorCodes.NotFound);
                productName = demand.Title; productSpec = demand.Spec; unit = demand.Unit;
            }

            var total = decimal.Round(q.Price * q.Quantity, 2);
            var orderNo = "O" + Guid.NewGuid().ToString("N")[..31];
            var orderId = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO trade_order (order_no, client_order_id, quote_id, buyer_id, seller_id, total_amount, status, delivery_mode, address_snapshot, remark, is_demo)
                VALUES (@no, @coid, @qid, @b, @s, @amt, 'PENDING_CONFIRM', @dm, @addr, @remark, @demo); SELECT LAST_INSERT_ID();",
                new { no = orderNo, coid = clientOrderId, qid = quoteId, b = q.BuyerId, s = q.SellerId, amt = total,
                    dm = req.DeliveryMode, addr = req.AddressSnapshot, remark = req.Remark, demo = _isDemo }, tx);
            // 订单明细快照: 保存成交时商品名称/规格/价格/数量
            await conn.ExecuteAsync(@"
                INSERT INTO trade_order_item (order_id, supply_id, product_name, spec, quantity, unit, price, price_unit, amount)
                VALUES (@oid, @sid, @name, @spec, @q, @unit, @price, @punit, @amt)",
                new { oid = orderId, sid = snapshotSupplyId, name = productName, spec = productSpec,
                    q = q.Quantity, unit = unit, price = q.Price, punit = q.PriceUnit, amt = total }, tx);
            await conn.ExecuteAsync("UPDATE trade_quote SET status='DEALED', order_id=@oid WHERE id=@qid", new { oid = orderId, qid = quoteId }, tx);
            await conn.ExecuteAsync(
                "INSERT INTO trade_order_status_log (order_id, from_status, to_status, operator_id, note) VALUES (@oid, NULL, 'PENDING_CONFIRM', @uid, '由报价生成订单')",
                new { oid = orderId, uid = buyerId }, tx);
            tx.Commit();
            var newOrderId = orderId;
            await _messages.SendAsync(q.SellerId, buyerId, "ORDER", "新订单待确认", $"订单 {orderNo} 已创建, 请尽快确认", newOrderId);
            return await GetOrderByIdAsync(newOrderId, buyerId);
        }
        catch
        {
            if (tx.Connection != null) tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 从聊天直接下单: 买方对指定供应/需求按给定价格数量立即成交。
    /// 为保持订单必须来自报价的不变量, 先生成一条状态为 ACCEPTED 的报价, 再走正常下单流程。
    /// </summary>
    public async Task<Dtos.OrderDto> CreateDirectOrderAsync(long buyerId,
        long? supplyId, long? demandId, decimal price, string priceUnit, decimal quantity,
        string? deliveryMode, string? addressSnapshot, string? remark, long? conversationId = null,
        string? clientOrderId = null)
    {
        if (price <= 0) throw new BizException("成交单价必须大于 0");
        if (quantity <= 0) throw new BizException("成交数量必须大于 0");
        if (supplyId == null && demandId == null) throw new BizException("必须指定供应商品或采购需求");
        if (supplyId != null && demandId != null) throw new BizException("只能指定供应商品或采购需求其一");

        // 幂等前置查重: 直购每次会先建一条 ACCEPTED 报价, 若命中幂等键却在建 quote 之后才发现,
        // 会给重复请求留下一条悬空报价。这里在源头就拦住 —— 已建过订单直接返回, 不建新 quote。
        var coid = string.IsNullOrWhiteSpace(clientOrderId) ? null : clientOrderId.Trim();
        if (coid != null)
        {
            using var chk = _db.Create();
            var dupId = await chk.ExecuteScalarAsync<long?>(
                "SELECT id FROM trade_order WHERE buyer_id=@b AND client_order_id=@c LIMIT 1",
                new { b = buyerId, c = coid });
            if (dupId != null) return await GetOrderByIdAsync(dupId.Value, buyerId);
        }

        long sellerId;
        using (var conn = _db.Create())
        {
            if (supplyId != null)
            {
                var s = await conn.QueryFirstOrDefaultAsync<(long Publisher, string Status)>(
                    "SELECT publisher_id, status FROM product_supply WHERE id=@id", new { id = supplyId });
                if (s == default) throw new BizException("供应商品不存在", ErrorCodes.NotFound);
                if (s.Status != "ACTIVE") throw new BizException("该供应信息当前不可交易");
                if (s.Publisher == buyerId) throw new BizException("不能对自己的商品下单");
                sellerId = s.Publisher;
            }
            else
            {
                var d = await conn.QueryFirstOrDefaultAsync<(long Publisher, string Status)>(
                    "SELECT publisher_id, status FROM purchase_demand WHERE id=@id", new { id = demandId });
                if (d == default) throw new BizException("采购需求不存在", ErrorCodes.NotFound);
                if (d.Status != "ACTIVE") throw new BizException("该采购需求当前不可交易");
                if (d.Publisher == buyerId) throw new BizException("不能对自己的采购需求下单");
                sellerId = d.Publisher;
            }
        }

        long quoteId;
        using (var conn = _db.Create())
        {
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                await ValidateQuantityAsync(conn, supplyId, demandId, quantity, price, priceUnit, tx);
                var quoteNo = "Q" + Guid.NewGuid().ToString("N")[..31];
                quoteId = await conn.ExecuteScalarAsync<long>(@"
                    INSERT INTO trade_quote (quote_no, supply_id, demand_id, buyer_id, seller_id, initiator_id, price, price_unit, quantity, remark, status, conversation_id)
                    VALUES (@no, @sid, @did, @b, @s, @b, @price, @punit, @q, @remark, 'ACCEPTED', @conv); SELECT LAST_INSERT_ID();",
                    new { no = quoteNo, sid = supplyId, did = demandId, b = buyerId, s = sellerId,
                        price, punit = priceUnit, q = quantity, remark, conv = conversationId }, tx);
                tx.Commit();
            }
            catch
            {
                if (tx.Connection != null) tx.Rollback();
                throw;
            }
        }

        return await CreateOrderFromQuoteAsync(buyerId, quoteId,
            new Dtos.OrderCreateFromQuoteRequest(deliveryMode, addressSnapshot, remark, clientOrderId));
    }

    /// <summary>按报价ID查询其关联的会话ID (无则返回 null)。</summary>
    public async Task<long?> GetQuoteConversationIdAsync(long quoteId)
    {
        using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<long?>(
            "SELECT conversation_id FROM trade_quote WHERE id=@id", new { id = quoteId });
    }

    /// <summary>将报价关联到指定会话 (还价生成的新报价复用原会话)。</summary>
    public async Task LinkQuoteConversationAsync(long quoteId, long conversationId)
    {
        using var conn = _db.Create();
        await conn.ExecuteAsync(
            "UPDATE trade_quote SET conversation_id=@conv WHERE id=@id AND conversation_id IS NULL",
            new { id = quoteId, conv = conversationId });
    }

    /// <summary>订单状态机: 定义合法流转, 状态校验在后端强制执行。</summary>
    private static readonly Dictionary<string, string[]> OrderTransitions = new()
    {
        ["PENDING_CONFIRM"] = new[] { "TO_PAY", "CANCELLED" },   // 卖方确认后进入待付款; 任一方可取消
        ["TO_PAY"] = new[] { "TO_SHIP", "CANCELLED" },           // 支付成功后进入待发货
        ["TO_SHIP"] = new[] { "SHIPPING" },                      // 卖方发货
        ["SHIPPING"] = new[] { "COMPLETED", "AFTER_SALE" },      // 买方确认收货 / 申请售后
        ["COMPLETED"] = new[] { "AFTER_SALE" },
        ["AFTER_SALE"] = Array.Empty<string>(),
        ["CANCELLED"] = Array.Empty<string>(),
    };

    public async Task<Dtos.OrderDto> ChangeOrderStatusAsync(long userId, long orderId, string target, string? reason, string? note)
    {
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var o = await conn.QueryFirstOrDefaultAsync<(long Id, long BuyerId, long SellerId, string Status)>(
                "SELECT id, buyer_id, seller_id, status FROM trade_order WHERE id=@id FOR UPDATE",
                new { id = orderId }, tx);
            if (o == default) throw new BizException("订单不存在", ErrorCodes.NotFound);
            var isBuyer = o.BuyerId == userId;
            var isSeller = o.SellerId == userId;
            if (!isBuyer && !isSeller) throw new BizException("无权操作该订单", ErrorCodes.Forbidden);

            // 未支付不得进入履约: TO_SHIP 需存在成功支付记录
            if (target == "TO_SHIP")
            {
                var paid = await conn.ExecuteScalarAsync<bool>(
                    "SELECT EXISTS(SELECT 1 FROM payment_record WHERE order_id=@oid AND status='SUCCESS')",
                    new { oid = orderId }, tx);
                if (!paid) throw new BizException("订单尚未支付, 不能进入发货环节", ErrorCodes.Conflict);
            }

            if (!OrderTransitions.TryGetValue(o.Status, out var allowed) || !allowed.Contains(target))
                throw new BizException($"订单状态 [{o.Status}] 不允许变更为 [{target}]", ErrorCodes.Conflict);

            if (target == "TO_PAY" && !isSeller)
                throw new BizException("只有卖方可以确认订单", ErrorCodes.Forbidden);
            if (target == "TO_SHIP" && !isBuyer)
                throw new BizException("只有买方可以发起支付确认", ErrorCodes.Forbidden);
            if (target == "SHIPPING" && !isSeller)
                throw new BizException("只有卖方可以发货", ErrorCodes.Forbidden);
            if (target == "COMPLETED" && !isBuyer)
                throw new BizException("只有买方可以确认收货", ErrorCodes.Forbidden);
            if (target == "AFTER_SALE" && !isBuyer)
                throw new BizException("只有买方可以申请售后", ErrorCodes.Forbidden);
            if (target == "CANCELLED" && !isBuyer && o.Status != "PENDING_CONFIRM")
                throw new BizException("仅待确认订单卖方可取消", ErrorCodes.Forbidden);

            if (target == "CANCELLED")
            {
                // 已收款的订单不允许直接取消: 否则会出现"钱已到账 + 订单已取消 + 无退款"的死局。
                // 注: 支付回调与取消并发进入时仍存在窄窗口, 需由退款补偿任务兜底(见 QA 报告)
                var paid = await conn.ExecuteScalarAsync<bool>(
                    "SELECT EXISTS(SELECT 1 FROM payment_record WHERE order_id=@oid AND status='SUCCESS')",
                    new { oid = orderId }, tx);
                if (paid)
                    throw new BizException("订单已支付, 不能直接取消, 请申请售后或联系管理员退款", ErrorCodes.Conflict);
                await ReleaseStockAsync(conn, tx, orderId);
            }

            var timeCol = target switch
            {
                "SHIPPING" => "shipped_at = NOW(3)",
                "COMPLETED" => "completed_at = NOW(3)",
                "CANCELLED" => "cancelled_at = NOW(3)",
                _ => null,
            };
            var sql = $"UPDATE trade_order SET status=@st{(timeCol != null ? $", {timeCol}" : "")}" +
                      (target == "CANCELLED" ? ", cancel_reason=@reason" : "") + " WHERE id=@id";
            await conn.ExecuteAsync(sql, new { st = target, id = orderId, reason }, tx);
            await conn.ExecuteAsync(
                "INSERT INTO trade_order_status_log (order_id, from_status, to_status, operator_id, note) VALUES (@oid, @from, @to, @uid, @note)",
                new { oid = orderId, from = o.Status, to = target, uid = userId, note }, tx);
            tx.Commit();
            var other = isBuyer ? o.SellerId : o.BuyerId;
            await _messages.SendAsync(other, userId, "ORDER", "订单状态更新", $"订单 #{orderId} 状态变更为 {target}{(reason != null ? $", 原因: {reason}" : "")}", orderId);
            return await GetOrderByIdAsync(orderId, userId);
        }
        catch
        {
            if (tx.Connection != null) tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 释放下单时占用的库存。只有 supply_id 非空的明细才需要回补(采购需求成交不扣供应库存)。
    /// 调用方必须已在该订单的行锁事务内, 且已确认订单尚未回补过(取消只能发生一次)。
    /// </summary>
    private static async Task ReleaseStockAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, long orderId)
    {
        await conn.ExecuteAsync(
            @"UPDATE product_supply s
              SET s.quantity = s.quantity + (
                    SELECT COALESCE(SUM(i.quantity), 0) FROM trade_order_item i
                    WHERE i.order_id = @oid AND i.supply_id = s.id),
                  s.status = CASE WHEN s.status = 'SOLD_OUT' THEN 'ACTIVE' ELSE s.status END
              WHERE s.id IN (
                    SELECT supply_id FROM trade_order_item
                    WHERE order_id = @oid AND supply_id IS NOT NULL)", new { oid = orderId }, tx);
    }

    /// <summary>
    /// 超时订单自动关闭(后台任务调用)。只处理已被支付之外的两种"卡住"状态:
    /// PENDING_CONFIRM 卖方久不确认、TO_PAY 买方久不付款。
    /// 必须在事务内 FOR UPDATE 重读状态: 用户可能同时手动确认/支付, 拿到的最新状态不是待处理态就跳过。
    /// </summary>
    /// <returns>true 表示本次调用实际关闭了该订单</returns>
    public async Task<bool> CancelExpiredAsync(long orderId, string reason)
    {
        using var conn = _db.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var o = await conn.QueryFirstOrDefaultAsync<(long Id, long BuyerId, long SellerId, string Status)>(
                "SELECT id, buyer_id, seller_id, status FROM trade_order WHERE id=@id FOR UPDATE",
                new { id = orderId }, tx);
            if (o == default) { tx.Rollback(); return false; }
            if (o.Status is not ("PENDING_CONFIRM" or "TO_PAY")) { tx.Rollback(); return false; }

            // 以"当前"状态复核超时阈值, 不信任调用方落快照时的判断。
            // worker 是"先取快照再逐条关闭": 快照与处理之间卖方可能刚点了确认
            // (PENDING_CONFIRM -> TO_PAY), 此时订单才进入待付款, 却仍会被按 48h 规则关掉,
            // 用户看到的是"超过 48 小时未确认"而实际刚确认过。待付款的计时基准是
            // "进入 TO_PAY 的时间", 不是下单时间。
            var timeoutHours = o.Status == "TO_PAY"
                ? Math.Max(1, _cfg.GetValue<int?>("Order:PayTimeoutHours") ?? 24)
                : Math.Max(1, _cfg.GetValue<int?>("Order:ConfirmTimeoutHours") ?? 48);
            var expired = await conn.ExecuteScalarAsync<bool>(@"
                SELECT EXISTS(SELECT 1 FROM trade_order o2 WHERE o2.id=@id AND CASE o2.status
                    WHEN 'TO_PAY' THEN COALESCE((SELECT MAX(l.created_at) FROM trade_order_status_log l
                        WHERE l.order_id=o2.id AND l.to_status='TO_PAY'), o2.created_at)
                    ELSE o2.created_at END < DATE_SUB(NOW(3), INTERVAL @h HOUR))",
                new { id = orderId, h = timeoutHours }, tx);
            if (!expired) { tx.Rollback(); return false; }
            // 关闭说明按当前状态生成, 避免"刚确认却被说成 48 小时未确认"
            reason = o.Status == "TO_PAY" ? $"超过 {timeoutHours} 小时未支付, 系统自动关闭" : $"超过 {timeoutHours} 小时未确认, 系统自动关闭";
            // 已支付的绝不自动取消: 钱在账上, 关闭会产生无退款死局
            var paid = await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM payment_record WHERE order_id=@oid AND status='SUCCESS')",
                new { oid = orderId }, tx);
            if (paid) { tx.Rollback(); return false; }

            await ReleaseStockAsync(conn, tx, orderId);
            await conn.ExecuteAsync(
                "UPDATE trade_order SET status='CANCELLED', cancelled_at=NOW(3), cancel_reason=@r WHERE id=@id",
                new { id = orderId, r = reason }, tx);
            await conn.ExecuteAsync(
                "INSERT INTO trade_order_status_log (order_id, from_status, to_status, operator_id, note) VALUES (@oid, @from, 'CANCELLED', NULL, @note)",
                new { oid = orderId, from = o.Status, note = reason }, tx);
            tx.Commit();

            // senderId 传 null: 系统消息没有发送人, 传 0 会撞外键
            await _messages.SendAsync(o.BuyerId, null, "ORDER", "订单已自动关闭", $"订单 #{orderId} {reason}, 占用的库存已释放", orderId);
            await _messages.SendAsync(o.SellerId, null, "ORDER", "订单已自动关闭", $"订单 #{orderId} {reason}, 占用的库存已释放", orderId);
            return true;
        }
        catch
        {
            if (tx.Connection != null) tx.Rollback();
            throw;
        }
    }

    public async Task<Dtos.PagedResult<Dtos.QuoteDto>> QueryQuotesAsync(long userId, string? scope, long? supplyId, long? demandId, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 10;
        var w = new List<string>();
        var p = new DynamicParameters();
        if (supplyId != null) { w.Add("q.supply_id=@sid"); p.Add("sid", supplyId); }
        if (demandId != null) { w.Add("q.demand_id=@did"); p.Add("did", demandId); }
        if (scope == "BUY") { w.Add("q.buyer_id=@uid"); p.Add("uid", userId); }
        else if (scope == "SELL") { w.Add("q.seller_id=@uid"); p.Add("uid", userId); }
        else { w.Add("(q.buyer_id=@uid OR q.seller_id=@uid)"); p.Add("uid", userId); }
        var where = w.Count == 0 ? "1=1" : " AND " + string.Join(" AND ", w);
        p.Add("limit", pageSize); p.Add("offset", Paging.Offset(page, pageSize));
        using var conn = _db.Create();
        var total = await conn.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM trade_quote q WHERE 1=1 {where}", p);
        var rows = (await conn.QueryAsync<QuoteRow>(string.Format(QuoteSelectSql, where), p)).ToList();
        return new Dtos.PagedResult<Dtos.QuoteDto>(rows.Select(MapQuote).ToList(), total, page, pageSize);
    }

    public async Task<Dtos.QuoteDto?> GetQuoteByIdAsync(long id)
    {
        using var conn = _db.Create();
        var row = await conn.QueryFirstOrDefaultAsync<QuoteRow>(string.Format(QuoteSelectSql, " AND q.id=@id"), new { id, limit = 1, offset = 0 });
        return row == null ? null : MapQuote(row);
    }

    private static Dtos.QuoteDto MapQuote(QuoteRow r) =>
        new(r.Id, r.QuoteNo, r.SupplyId, r.SupplyTitle, r.DemandId, r.DemandTitle, r.BuyerId, r.BuyerName,
            r.SellerId, r.SellerName, r.InitiatorId, r.Price, r.PriceUnit, r.Quantity, r.Remark, r.Status, r.OrderId, r.CreatedAt);

    public async Task<Dtos.PagedResult<Dtos.OrderDto>> QueryOrdersAsync(long userId, string? role, string? status, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 10;
        var w = new List<string>();
        var p = new DynamicParameters();
        if (role == "BUY") { w.Add("o.buyer_id=@uid"); p.Add("uid", userId); }
        else if (role == "SELL") { w.Add("o.seller_id=@uid"); p.Add("uid", userId); }
        else { w.Add("(o.buyer_id=@uid OR o.seller_id=@uid)"); p.Add("uid", userId); }
        if (!string.IsNullOrWhiteSpace(status)) { w.Add("o.status=@st"); p.Add("st", status); }
        var where = string.Join(" AND ", w);
        p.Add("limit", pageSize);
        p.Add("offset", Paging.Offset(page, pageSize));
        using var conn = _db.Create();
        var total = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM trade_order o WHERE {where}", p);
        var ids = (await conn.QueryAsync<long>(
            $@"SELECT o.id FROM trade_order o WHERE {where} ORDER BY o.created_at DESC LIMIT @limit OFFSET @offset",
            p)).ToList();
        var items = new List<Dtos.OrderDto>();
        foreach (var id in ids) items.Add(await GetOrderByIdAsync(id, userId));
        return new Dtos.PagedResult<Dtos.OrderDto>(items, total, page, pageSize);
    }

    public async Task<Dtos.OrderDto> GetOrderByIdAsync(long id, long viewerId)
    {
        using var conn = _db.Create();
        var o = await conn.QueryFirstOrDefaultAsync<(long Id, string OrderNo, long QuoteId, long BuyerId, long SellerId,
            decimal TotalAmount, string Status, string? PayChannel, DateTime? PaidAt, DateTime? ShippedAt, DateTime? CompletedAt,
            DateTime? CancelledAt, string? CancelReason, string? DeliveryMode, string? AddressSnapshot, string? Remark, DateTime CreatedAt)>(
            @"SELECT id, order_no, quote_id, buyer_id, seller_id, total_amount, status, pay_channel, paid_at, shipped_at,
                     completed_at, cancelled_at, cancel_reason, delivery_mode, address_snapshot, remark, created_at
              FROM trade_order WHERE id=@id", new { id });
        if (o == default) throw new BizException("订单不存在", ErrorCodes.NotFound);
        if (viewerId != o.BuyerId && viewerId != o.SellerId)
        {
            var isAdmin = await conn.ExecuteScalarAsync<bool>("SELECT role='ADMIN' FROM sys_user WHERE id=@id", new { id = viewerId });
            if (!isAdmin) throw new BizException("无权查看该订单", ErrorCodes.Forbidden);
        }
        var names = await conn.QueryFirstAsync<(string Buyer, string Seller)>(
            @"SELECT (SELECT nickname FROM sys_user WHERE id=@b) Buyer, (SELECT nickname FROM sys_user WHERE id=@s) Seller",
            new { b = o.BuyerId, s = o.SellerId });
        var items = (await conn.QueryAsync<Dtos.OrderItemDto>(
            @"SELECT id, supply_id SupplyId, product_name ProductName, spec, quantity Quantity, unit, price Price, price_unit PriceUnit, amount Amount
              FROM trade_order_item WHERE order_id=@id ORDER BY id", new { id })).ToList();
        var logs = (await conn.QueryAsync<Dtos.StatusLogDto>(
            @"SELECT id, from_status FromStatus, to_status ToStatus, operator_id OperatorId, note, created_at CreatedAt
              FROM trade_order_status_log WHERE order_id=@id ORDER BY id", new { id })).ToList();
        return new Dtos.OrderDto(o.Id, o.OrderNo, o.QuoteId, o.BuyerId, names.Buyer, o.SellerId, names.Seller,
            o.TotalAmount, o.Status, o.PayChannel, o.PaidAt, o.ShippedAt, o.CompletedAt, o.CancelledAt, o.CancelReason,
            o.DeliveryMode, o.AddressSnapshot, o.Remark, o.CreatedAt, items, logs);
    }
}
