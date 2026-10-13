using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

// AgroMarket 后端集成冒烟测试（无需外部测试框架）
// 运行前请确保后端已启动（默认 http://127.0.0.1:8080）与数据库已初始化。
// 用法: dotnet run  [-- <baseUrl>]

var baseUrl = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("AGRO_API") ?? "http://127.0.0.1:8080";
var passed = 0;
var failed = 0;
var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(20) };

void Check(string name, bool ok, string? detail = null)
{
    if (ok) { passed++; Console.WriteLine($"  PASS  {name}"); }
    else { failed++; Console.WriteLine($"  FAIL  {name}{(detail != null ? " -> " + detail : "")}"); }
}

async Task<(HttpStatusCode status, JsonElement body)> Call(HttpMethod method, string url, object? payload = null, string? token = null)
{
    var req = new HttpRequestMessage(method, url);
    if (payload != null) req.Content = JsonContent.Create(payload);
    if (token != null) req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    var res = await http.SendAsync(req);
    var text = await res.Content.ReadAsStringAsync();
    JsonElement body = default;
    try { body = JsonDocument.Parse(text).RootElement.Clone(); } catch { }
    return (res.StatusCode, body);
}

int Code(JsonElement b) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty("code", out var c) ? c.GetInt32() : -999;
string Msg(JsonElement b) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty("msg", out var m) ? (m.GetString() ?? "") : "";
JsonElement Data(JsonElement b) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty("data", out var d) ? d : default;

async Task<string> Login(string phone, string pwd)
{
    var (_, b) = await Call(HttpMethod.Post, "/api/auth/login/password", new { phone, password = pwd });
    return Data(b).GetProperty("token").GetString()!;
}

Console.WriteLine($"AgroMarket 集成测试 @ {baseUrl}\n");

// 1. 健康检查
try
{
    var res = await http.GetAsync("/health");
    Check("健康检查 /health 返回 200", res.StatusCode == HttpStatusCode.OK);
}
catch (Exception ex) { Check("健康检查 /health", false, ex.Message); }

// 2. 未登录访问受保护端点应 401
foreach (var p in new[] { "/api/user/me", "/api/orders", "/api/quotes", "/api/admin/stats" })
{
    var (status, b) = await Call(HttpMethod.Get, p);
    Check($"未登录 {p} 返回 401", status == HttpStatusCode.Unauthorized && Code(b) == 401, $"status={(int)status} code={Code(b)}");
}

// 3. 登录
string adminToken = "", sellerToken = "", buyerToken = "";
try
{
    adminToken = await Login("13800000001", "Abc123456");   // ADMIN
    sellerToken = await Login("13800000002", "Abc123456");  // FARMER (卖家)
    buyerToken = await Login("13800000003", "Abc123456");   // BUYER (买家)
    Check("三类账号密码登录成功", adminToken.Length > 20 && sellerToken.Length > 20 && buyerToken.Length > 20);
}
catch (Exception ex) { Check("密码登录", false, ex.Message); }

// 4. 管理员统计（真实成交额与演示成交额区分）
if (adminToken.Length > 0)
{
    var (status, b) = await Call(HttpMethod.Get, "/api/admin/stats", null, adminToken);
    var hasDemo = Data(b).ValueKind == JsonValueKind.Object && Data(b).TryGetProperty("demoDealAmount", out _);
    Check("管理员可获取统计且区分演示成交额", status == HttpStatusCode.OK && Code(b) == 0 && hasDemo);
}

// 5. 非管理员访问后台应 403
if (buyerToken.Length > 0)
{
    var (status, b) = await Call(HttpMethod.Get, "/api/admin/stats", null, buyerToken);
    Check("普通用户访问 /api/admin/stats 返回 403", status == HttpStatusCode.Forbidden && Code(b) == 403, $"status={(int)status}");
}

// 6. 公开供应列表
long supplyId = 0;
{
    var (status, b) = await Call(HttpMethod.Get, "/api/supplies?page=1&pageSize=5");
    var items = Data(b);
    if (items.ValueKind == JsonValueKind.Object && items.TryGetProperty("items", out var arr) && arr.GetArrayLength() > 0)
    {
        var first = arr[0];
        supplyId = first.GetProperty("id").GetInt64();
    }
    Check("公开供应列表可访问且含数据", status == HttpStatusCode.OK && Code(b) == 0 && supplyId > 0, $"supplyId={supplyId}");
}

// 7. 报价 -> 卖家接受 -> 买方下单 -> 卖家确认 -> 支付前状态冻结
if (supplyId > 0 && sellerToken.Length > 0 && buyerToken.Length > 0)
{
    // 选择卖家(用户2)发布的供应，以便用卖家令牌接受报价
    var (_, lb) = await Call(HttpMethod.Get, "/api/supplies?page=1&pageSize=20");
    long targetId = 0; long publisherId = 0; decimal minQty = 1; decimal availQty = 1; string punit = "元/斤";
    if (Data(lb).TryGetProperty("items", out var arr))
    {
        foreach (var it in arr.EnumerateArray())
        {
            if (it.GetProperty("publisherId").GetInt64() == 2)
            {
                targetId = it.GetProperty("id").GetInt64();
                publisherId = it.GetProperty("publisherId").GetInt64();
                availQty = it.GetProperty("quantity").GetDecimal();
                if (it.TryGetProperty("minOrderQty", out var mo) && mo.ValueKind == JsonValueKind.Number) minQty = mo.GetDecimal();
                if (it.TryGetProperty("priceUnit", out var pu) && pu.ValueKind == JsonValueKind.String) punit = pu.GetString()!;
                break;
            }
        }
    }
    if (targetId > 0)
    {
        var testQty = Math.Max(minQty, 1);
        if (testQty > availQty) testQty = availQty;
        var (s1, qb) = await Call(HttpMethod.Post, "/api/quotes", new { supplyId = targetId, price = 1.25, priceUnit = punit, quantity = testQty, remark = "集成测试" }, buyerToken);
        long qid = s1 == HttpStatusCode.OK && Code(qb) == 0 && Data(qb).TryGetProperty("id", out var qe) ? qe.GetInt64() : 0;
        Check("买方创建报价", qid > 0, $"targetId={targetId} publisher={publisherId} code={Code(qb)} msg={Msg(qb)}");

        if (qid > 0)
        {
            // 发起方自己接受应被拒绝
            var (_, selfAccept) = await Call(HttpMethod.Post, $"/api/quotes/{qid}/accept", null, buyerToken);
            Check("发起方不能接受自己的报价(403)", Code(selfAccept) == 403);

            var (s2, ab) = await Call(HttpMethod.Post, $"/api/quotes/{qid}/accept", null, sellerToken);
            Check("卖方接受报价", s2 == HttpStatusCode.OK && Code(ab) == 0, $"code={Code(ab)} msg={Msg(ab)}");

            var (s3, ob) = await Call(HttpMethod.Post, $"/api/orders/from-quote/{qid}", new { deliveryMode = "SELF_PICKUP", remark = "集成测试" }, buyerToken);
            long oid = s3 == HttpStatusCode.OK && Code(ob) == 0 && Data(ob).TryGetProperty("id", out var oe) ? oe.GetInt64() : 0;
            Check("买方由报价创建订单", oid > 0, $"code={Code(ob)}");

            if (oid > 0)
            {
                var (_, c1) = await Call(HttpMethod.Post, $"/api/orders/{oid}/status/confirm", null, sellerToken);
                Check("卖方确认订单进入 TO_PAY", Code(c1) == 0, $"code={Code(c1)}");

                // 未支付前不得发货
                var (_, shipBefore) = await Call(HttpMethod.Post, $"/api/orders/{oid}/status/ship", null, sellerToken);
                Check("未支付时发货被拒绝(409)", Code(shipBefore) == 409, $"code={Code(shipBefore)}");

                // 未配置的真实渠道不可用
                var (_, wx) = await Call(HttpMethod.Post, "/api/payments", new { orderId = oid, channel = "WECHAT" }, buyerToken);
                Check("未配置的微信支付渠道被拒绝(501)", Code(wx) == 501, $"code={Code(wx)}");

                // 非买方不能支付
                var (_, payBySeller) = await Call(HttpMethod.Post, "/api/payments", new { orderId = oid, channel = "MOCK" }, sellerToken);
                Check("非买方不能发起支付(403)", Code(payBySeller) == 403, $"code={Code(payBySeller)}");

                // Mock 支付成功 -> TO_SHIP, 写入 paid_at
                var (_, pay) = await Call(HttpMethod.Post, "/api/payments", new { orderId = oid, channel = "MOCK" }, buyerToken);
                var paidOk = Code(pay) == 0 && Data(pay).TryGetProperty("paidNow", out var pn) && pn.GetBoolean();
                Check("模拟支付成功并写支付记录", paidOk, $"code={Code(pay)} msg={Msg(pay)}");

                var (_, dd) = await Call(HttpMethod.Get, $"/api/orders/{oid}", null, buyerToken);
                var stNow = Data(dd).TryGetProperty("status", out var sn) ? sn.GetString() : "";
                var paidAt = Data(dd).TryGetProperty("paidAt", out var pa) ? pa : default;
                Check("支付后订单进入 TO_SHIP 且记录支付时间", stNow == "TO_SHIP" && paidAt.ValueKind == JsonValueKind.String, $"status={stNow}");

                var (_, ship) = await Call(HttpMethod.Post, $"/api/orders/{oid}/status/ship", null, sellerToken);
                Check("卖方发货进入 SHIPPING", Code(ship) == 0, $"code={Code(ship)}");

                var (_, recv) = await Call(HttpMethod.Post, $"/api/orders/{oid}/status/receive", null, buyerToken);
                Check("买方确认收货进入 COMPLETED", Code(recv) == 0, $"code={Code(recv)}");

                var (_, pays) = await Call(HttpMethod.Get, $"/api/payments/order/{oid}", null, buyerToken);
                var hasMock = false;
                if (Data(pays).ValueKind == JsonValueKind.Array)
                    foreach (var p in Data(pays).EnumerateArray())
                        if (p.TryGetProperty("channel", out var ch) && ch.GetString() == "MOCK"
                            && p.GetProperty("status").GetString() == "SUCCESS") hasMock = true;
                Check("订单支付记录渠道为 MOCK/SUCCESS", hasMock);
            }
        }
    }
    else Check("找到可用于测试的供应信息", false);
}

// 8. 采购需求报价 -> 转订单
if (sellerToken.Length > 0 && buyerToken.Length > 0)
{
    var (_, db) = await Call(HttpMethod.Get, "/api/demands?page=1&pageSize=20");
    long dId = 0; decimal dQty = 1; string dPunit = "元/斤";
    if (Data(db).TryGetProperty("items", out var darr))
    {
        foreach (var it in darr.EnumerateArray())
        {
            if (it.GetProperty("publisherId").GetInt64() == 3)
            {
                dId = it.GetProperty("id").GetInt64();
                dQty = it.GetProperty("quantity").GetDecimal();
                if (it.TryGetProperty("priceUnit", out var pu) && pu.ValueKind == JsonValueKind.String) dPunit = pu.GetString()!;
                break;
            }
        }
    }
    if (dId > 0)
    {
        var qty = Math.Min(dQty, 100m);
        var (_, qb2) = await Call(HttpMethod.Post, "/api/quotes", new { demandId = dId, price = 1.3m, priceUnit = dPunit, quantity = qty, remark = "供货报价" }, sellerToken);
        long qid2 = Code(qb2) == 0 && Data(qb2).TryGetProperty("id", out var qe2) ? qe2.GetInt64() : 0;
        Check("卖家对采购需求报价", qid2 > 0, $"code={Code(qb2)} msg={Msg(qb2)}");
        if (qid2 > 0)
        {
            var (_, ab2) = await Call(HttpMethod.Post, $"/api/quotes/{qid2}/accept", null, buyerToken);
            Check("采购方接受报价", Code(ab2) == 0, $"code={Code(ab2)} msg={Msg(ab2)}");

            var (_, ob2) = await Call(HttpMethod.Post, $"/api/orders/from-quote/{qid2}", new { deliveryMode = "DELIVERY", remark = "采购集成测试" }, buyerToken);
            long oid2 = Code(ob2) == 0 && Data(ob2).TryGetProperty("id", out var oe2) ? oe2.GetInt64() : 0;
            Check("采购需求报价转订单成功", oid2 > 0, $"code={Code(ob2)} msg={Msg(ob2)}");
            if (oid2 > 0)
            {
                var (_, od) = await Call(HttpMethod.Get, $"/api/orders/{oid2}", null, buyerToken);
                var itemName = Data(od).TryGetProperty("items", out var its) && its.GetArrayLength() > 0 ? its[0].GetProperty("productName").GetString() : null;
                Check("采购订单含商品快照(来自需求标题)", !string.IsNullOrEmpty(itemName));
            }
        }
    }
    else Check("找到买家发布的采购需求", false);
}

// 9. 站内聊天 (会话去重 / 消息幂等 / 未读 / 权限)
// 帮助函数: 把当前用户全部会话标记已读并隐藏, 消除跨轮次遗留状态对全局未读/会话总数的干扰
async Task ResetChatState(string token)
{
    var (_, l) = await Call(HttpMethod.Get, "/api/chat/conversations?page=1&pageSize=100", null, token);
    if (Data(l).TryGetProperty("items", out var cs))
        foreach (var c in cs.EnumerateArray())
        {
            var id = c.GetProperty("id").GetInt64();
            await Call(HttpMethod.Post, $"/api/chat/conversations/{id}/read", new { lastReadMessageId = long.MaxValue }, token);
            await Call(HttpMethod.Delete, $"/api/chat/conversations/{id}", null, token);
        }
}

if (sellerToken.Length > 0 && buyerToken.Length > 0 && adminToken.Length > 0)
{
    await ResetChatState(buyerToken);
    await ResetChatState(sellerToken);
    long convId = 0;
    var (s1, cb) = await Call(HttpMethod.Post, "/api/chat/conversations",
        new { peerUserId = 2, sourceType = "PRODUCT", sourceId = 1 }, buyerToken);
    if (Code(cb) == 0 && Data(cb).TryGetProperty("id", out var ce)) convId = ce.GetInt64();
    Check("买家发起会话", convId > 0, $"code={Code(cb)} msg={Msg(cb)}");

    if (convId > 0)
    {
        var (_, cb2) = await Call(HttpMethod.Post, "/api/chat/conversations",
            new { peerUserId = 2, sourceType = "PRODUCT", sourceId = 1 }, buyerToken);
        var convId2 = Data(cb2).TryGetProperty("id", out var ce2) ? ce2.GetInt64() : 0;
        Check("重复发起返回同一会话(去重)", convId2 == convId);

        // 重置双方未读, 保证断言确定性
        await Call(HttpMethod.Post, $"/api/chat/conversations/{convId}/read", new { lastReadMessageId = long.MaxValue }, buyerToken);
        await Call(HttpMethod.Post, $"/api/chat/conversations/{convId}/read", new { lastReadMessageId = long.MaxValue }, sellerToken);

        var cmid = Guid.NewGuid().ToString("N");
        var (sm, mb) = await Call(HttpMethod.Post, $"/api/chat/conversations/{convId}/messages",
            new { clientMsgId = cmid, messageType = "TEXT", content = "集成测试: 有货吗?" }, buyerToken);
        long mid = Code(mb) == 0 && Data(mb).TryGetProperty("id", out var me) ? me.GetInt64() : 0;
        var fieldsOk = mid > 0
            && Data(mb).GetProperty("conversationId").GetInt64() == convId
            && Data(mb).GetProperty("senderId").GetInt64() == 3
            && Data(mb).GetProperty("messageType").GetString() == "TEXT";
        Check("买家发送消息且字段完整", fieldsOk, $"code={Code(mb)}");

        var (_, mb2) = await Call(HttpMethod.Post, $"/api/chat/conversations/{convId}/messages",
            new { clientMsgId = cmid, messageType = "TEXT", content = "集成测试: 有货吗?" }, buyerToken);
        var mid2 = Data(mb2).TryGetProperty("id", out var me2) ? me2.GetInt64() : 0;
        Check("相同 clientMsgId 幂等返回同一条", mid2 == mid);

        var (sEmpty, eb) = await Call(HttpMethod.Post, $"/api/chat/conversations/{convId}/messages",
            new { messageType = "TEXT", content = "   " }, buyerToken);
        Check("空文本消息被拒绝(400)", sEmpty == HttpStatusCode.BadRequest || Code(eb) == 400);

        var (sForbid, _) = await Call(HttpMethod.Get, $"/api/chat/conversations/{convId}/messages", null, adminToken);
        Check("非会话成员访问消息返回 403", sForbid == HttpStatusCode.Forbidden);

        var (_, ub) = await Call(HttpMethod.Get, "/api/chat/unread", null, sellerToken);
        Check("卖家未读数为 1", Code(ub) == 0 && Data(ub).GetInt64() == 1, $"unread={Data(ub)}");

        var (_, ml) = await Call(HttpMethod.Get, $"/api/chat/conversations/{convId}/messages", null, sellerToken);
        var gotIt = Data(ml).TryGetProperty("items", out var its2) && its2.GetArrayLength() > 0
            && its2[its2.GetArrayLength() - 1].GetProperty("content").GetString()!.Contains("有货吗");
        Check("卖家可读取消息且内容正确", gotIt);

        var (_, rb) = await Call(HttpMethod.Post, $"/api/chat/conversations/{convId}/read", new { lastReadMessageId = mid }, sellerToken);
        var (_, ub2) = await Call(HttpMethod.Get, "/api/chat/unread", null, sellerToken);
        Check("卖家标记已读后未读归零", Code(rb) == 0 && Data(ub2).GetInt64() == 0, $"unread={Data(ub2)}");

        var (_, rep) = await Call(HttpMethod.Post, $"/api/chat/conversations/{convId}/messages",
            new { clientMsgId = Guid.NewGuid().ToString("N"), messageType = "TEXT", content = "有货" }, sellerToken);
        var (_, ubBuyer) = await Call(HttpMethod.Get, "/api/chat/unread", null, buyerToken);
        Check("卖家回复后买家未读为 1", Code(rep) == 0 && Data(ubBuyer).GetInt64() == 1, $"unread={Data(ubBuyer)}");

        var (_, cl) = await Call(HttpMethod.Get, "/api/chat/conversations", null, sellerToken);
        var listOk = Data(cl).TryGetProperty("items", out var carr) && carr.GetArrayLength() > 0
            && carr[0].GetProperty("lastContent").GetString() == "有货";
        Check("会话列表含最后一条消息摘要", listOk);

        // 消息搜索: 会话内 + 全局
        var (sIn, inb) = await Call(HttpMethod.Get,
            $"/api/chat/search?keyword=有货&conversationId={convId}", null, sellerToken);
        var inItems = Data(inb);
        var inOk = Code(inb) == 0 && inItems.ValueKind == JsonValueKind.Array && inItems.GetArrayLength() >= 2
            && inItems[0].GetProperty("message").GetProperty("content").GetString()!.Contains("有货");
        Check("会话内搜索命中消息", inOk, $"count={(inItems.ValueKind == JsonValueKind.Array ? inItems.GetArrayLength() : -1)}");

        var (sGlob, gb) = await Call(HttpMethod.Get, "/api/chat/search?keyword=有货", null, sellerToken);
        var gItems = Data(gb);
        var gOk = Code(gb) == 0 && gItems.ValueKind == JsonValueKind.Array && gItems.GetArrayLength() >= 1
            && gItems[0].GetProperty("conversation").TryGetProperty("id", out _)
            && gItems[0].GetProperty("message").GetProperty("content").GetString()!.Contains("有货");
        Check("全局搜索返回命中会话+消息", gOk, $"count={(gItems.ValueKind == JsonValueKind.Array ? gItems.GetArrayLength() : -1)}");

        var (sNo, nb) = await Call(HttpMethod.Get, "/api/chat/search?keyword=%E4%B8%8D%E5%AD%98%E5%9C%A8%E7%9A%84%E8%AF%8Dxxx", null, sellerToken);
        Check("搜索无结果返回空数组", Code(nb) == 0 && Data(nb).ValueKind == JsonValueKind.Array && Data(nb).GetArrayLength() == 0);

        var (sEmptyKw, ekb) = await Call(HttpMethod.Get, "/api/chat/search?keyword=", null, sellerToken);
        Check("搜索空关键词返回 400", sEmptyKw == HttpStatusCode.BadRequest || Code(ekb) == 400);

        var (sOutsider, ob) = await Call(HttpMethod.Get, $"/api/chat/search?keyword=有货&conversationId={convId}", null, adminToken);
        Check("非成员会话内搜索返回 403", sOutsider == HttpStatusCode.Forbidden || Code(ob) == 403);

        var (_, hb) = await Call(HttpMethod.Delete, $"/api/chat/conversations/{convId}", null, buyerToken);
        var (_, clBuyer) = await Call(HttpMethod.Get, "/api/chat/conversations", null, buyerToken);
        var (_, clSeller) = await Call(HttpMethod.Get, "/api/chat/conversations", null, sellerToken);
        var buyerTotal = Data(clBuyer).GetProperty("total").GetInt64();
        var sellerTotal = Data(clSeller).GetProperty("total").GetInt64();
        Check("买家删除会话后自己列表不含、对方仍可见", Code(hb) == 0 && buyerTotal == 0 && sellerTotal >= 1, $"buyer={buyerTotal} seller={sellerTotal}");
    }

    var (_, selfB) = await Call(HttpMethod.Post, "/api/chat/conversations", new { peerUserId = 3 }, buyerToken);
    Check("不能与自己发起会话(400)", Code(selfB) == 400);
}

// 10. 实时通道 (WebSocket)
if (sellerToken.Length > 0 && buyerToken.Length > 0)
{
    var wsBase = baseUrl.Replace("http://", "ws://").Replace("https://", "wss://");
    using var ws = new ClientWebSocket();

    async Task<JsonElement?> WsRecv(int timeoutMs = 4000)
    {
        var buf = new byte[16 * 1024];
        using var cts = new CancellationTokenSource(timeoutMs);
        var ms = new MemoryStream();
        try
        {
            while (true)
            {
                var r = await ws.ReceiveAsync(buf, cts.Token);
                if (r.MessageType == WebSocketMessageType.Close) return null;
                ms.Write(buf, 0, r.Count);
                if (r.EndOfMessage) break;
            }
        }
        catch { return null; }
        try { return JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray())).RootElement.Clone(); }
        catch { return null; }
    }

    string? TypeOf(JsonElement? e) => e.HasValue && e.Value.TryGetProperty("type", out var t) ? t.GetString() : null;

    try
    {
        await ws.ConnectAsync(new Uri($"{wsBase}/ws/chat?token={Uri.EscapeDataString(sellerToken)}"), CancellationToken.None);
        Check("WebSocket 连接成功", ws.State == WebSocketState.Open);
    }
    catch (Exception ex) { Check("WebSocket 连接成功", false, ex.Message); }

    if (ws.State == WebSocketState.Open)
    {
        var hello = await WsRecv();
        Check("WS 收到 connected", TypeOf(hello) == "connected");

        await ws.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"ping\"}"), WebSocketMessageType.Text, true, CancellationToken.None);
        var pong = await WsRecv();
        Check("WS ping/pong 心跳", TypeOf(pong) == "pong");

        long wsConv = 0;
        var (_, wcb) = await Call(HttpMethod.Post, "/api/chat/conversations",
            new { peerUserId = 2, sourceType = "SUPPLY", sourceId = 1 }, buyerToken);
        if (Code(wcb) == 0 && Data(wcb).TryGetProperty("id", out var wce)) wsConv = wce.GetInt64();

        if (wsConv > 0)
        {
            var wmid = "ws-" + Guid.NewGuid().ToString("N");
            await Call(HttpMethod.Post, $"/api/chat/conversations/{wsConv}/messages",
                new { clientMsgId = wmid, messageType = "TEXT", content = "实时推送" }, buyerToken);
            var push = await WsRecv();
            var gotPush = TypeOf(push) == "message"
                && push!.Value.GetProperty("message").GetProperty("content").GetString() == "实时推送";
            Check("买家 REST 发送后卖家 WS 实时收到", gotPush);

            var outCid = "ws-out-" + Guid.NewGuid().ToString("N");
            var payload = JsonSerializer.Serialize(new { type = "send", conversationId = wsConv, clientMsgId = outCid, messageType = "TEXT", content = "WS回复" });
            await ws.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, CancellationToken.None);
            JsonElement? ack = null;
            for (var i = 0; i < 4 && ack is null; i++)
            {
                var f = await WsRecv();
                if (TypeOf(f) == "ack") ack = f;
            }
            Check("WS 发送收到 ack", ack.HasValue && ack.Value.GetProperty("clientMsgId").GetString() == outCid);
        }

        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); } catch { }
        if (wsConv > 0)
        {
            foreach (var tk in new[] { buyerToken, sellerToken })
            {
                await Call(HttpMethod.Post, $"/api/chat/conversations/{wsConv}/read", new { lastReadMessageId = long.MaxValue }, tk);
                await Call(HttpMethod.Delete, $"/api/chat/conversations/{wsConv}", null, tk);
            }
        }
    }
}

// 10b. 实时通道: read/delivered/typing/recall 推送
if (sellerToken.Length > 0 && buyerToken.Length > 0)
{
    Console.WriteLine("\n--- 10b. 实时推送 (已读/送达/输入/撤回) ---");
    var wsBase2 = baseUrl.Replace("http://", "ws://").Replace("https://", "wss://");
    using var ws2 = new ClientWebSocket();

    async Task<JsonElement?> WsRecv2(int timeoutMs = 4000)
    {
        var buf = new byte[16 * 1024];
        using var cts = new CancellationTokenSource(timeoutMs);
        var ms = new MemoryStream();
        try
        {
            while (true)
            {
                var r = await ws2.ReceiveAsync(buf, cts.Token);
                if (r.MessageType == WebSocketMessageType.Close) return null;
                ms.Write(buf, 0, r.Count);
                if (r.EndOfMessage) break;
            }
        }
        catch { return null; }
        try { return JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray())).RootElement.Clone(); }
        catch { return null; }
    }
    string? TypeOf2(JsonElement? e) => e.HasValue && e.Value.TryGetProperty("type", out var t) ? t.GetString() : null;
    async Task<JsonElement?> WsUntil(string type, int tries = 8)
    {
        for (var i = 0; i < tries; i++)
        {
            var e = await WsRecv2();
            if (e is null) return null;
            if (TypeOf2(e) == type) return e;
        }
        return null;
    }

    try
    {
        await ws2.ConnectAsync(new Uri($"{wsBase2}/ws/chat?token={Uri.EscapeDataString(sellerToken)}"), CancellationToken.None);
        Check("实时推送通道连接成功", ws2.State == WebSocketState.Open);
    }
    catch (Exception ex) { Check("实时推送通道连接成功", false, ex.Message); }

    if (ws2.State == WebSocketState.Open)
    {
        await WsRecv2(); // connected

        long pcConv = 0;
        var pcSrcId = (DateTime.Now.Ticks % 400_000) + 600_000;
        var (_, pcCb) = await Call(HttpMethod.Post, "/api/chat/conversations",
            new { peerUserId = 2, sourceType = "SUPPLY", sourceId = pcSrcId }, buyerToken);
        if (Code(pcCb) == 0 && Data(pcCb).TryGetProperty("id", out var pcCe)) pcConv = pcCe.GetInt64();
        Check("推送测试会话创建成功", pcConv > 0, $"code={Code(pcCb)}");

        if (pcConv > 0)
        {
            var (_, pm) = await Call(HttpMethod.Post, $"/api/chat/conversations/{pcConv}/messages",
                new { clientMsgId = Guid.NewGuid().ToString("N"), messageType = "TEXT", content = "推送测试消息" }, buyerToken);
            long pcMid = Data(pm).TryGetProperty("id", out var pme) ? pme.GetInt64() : 0;

            var msgPush = await WsUntil("message");
            Check("买家发送后卖家收到 message 推送", TypeOf2(msgPush) == "message");

            await Call(HttpMethod.Post, $"/api/chat/conversations/{pcConv}/typing", new { typing = true }, buyerToken);
            var typingPush = await WsUntil("typing");
            Check("买家输入中卖家收到 typing 推送",
                TypeOf2(typingPush) == "typing" && typingPush!.Value.GetProperty("typing").GetBoolean());

            if (pcMid > 0)
            {
                await Call(HttpMethod.Post, $"/api/chat/conversations/{pcConv}/delivered",
                    new { lastDeliveredMessageId = pcMid }, buyerToken);
                var devPush = await WsUntil("delivered");
                Check("送达回执推送 delivered",
                    TypeOf2(devPush) == "delivered" && devPush!.Value.GetProperty("lastDeliveredMessageId").GetInt64() == pcMid);

                await Call(HttpMethod.Post, $"/api/chat/conversations/{pcConv}/read",
                    new { lastReadMessageId = pcMid }, buyerToken);
                var readPush = await WsUntil("read");
                Check("已读回执推送 read",
                    TypeOf2(readPush) == "read" && readPush!.Value.GetProperty("lastReadMessageId").GetInt64() == pcMid);

                await Call(HttpMethod.Post, $"/api/chat/messages/{pcMid}/recall", null, buyerToken);
                var recallPush = await WsUntil("recall");
                Check("撤回推送 recall",
                    TypeOf2(recallPush) == "recall" && recallPush!.Value.GetProperty("messageId").GetInt64() == pcMid);
            }

            foreach (var tk in new[] { buyerToken, sellerToken })
            {
                await Call(HttpMethod.Post, $"/api/chat/conversations/{pcConv}/read", new { lastReadMessageId = long.MaxValue }, tk);
                await Call(HttpMethod.Delete, $"/api/chat/conversations/{pcConv}", null, tk);
            }
        }

        try { await ws2.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); } catch { }
    }
}

// 11. 微信式富消息 (图片 / 引用 / 撤回 / 送达 / 输入状态 / 对端游标)
if (sellerToken.Length > 0 && buyerToken.Length > 0)
{
    long wxfConv = 0;
    var wxfSrcId = DateTime.Now.Ticks % 1_000_000;
    var (_, wxfCb) = await Call(HttpMethod.Post, "/api/chat/conversations",
        new { peerUserId = 2, sourceType = "DEMAND", sourceId = wxfSrcId }, buyerToken);
    if (Code(wxfCb) == 0 && Data(wxfCb).TryGetProperty("id", out var wxfCe)) wxfConv = wxfCe.GetInt64();
    Check("富消息会话创建成功", wxfConv > 0, $"code={Code(wxfCb)}");

    if (wxfConv > 0)
    {
        var (_, t1) = await Call(HttpMethod.Post, $"/api/chat/conversations/{wxfConv}/messages",
            new { clientMsgId = Guid.NewGuid().ToString("N"), messageType = "TEXT", content = "被引用的原消息" }, buyerToken);
        long originId = Data(t1).TryGetProperty("id", out var oe) ? oe.GetInt64() : 0;

        var (si, ib) = await Call(HttpMethod.Post, $"/api/chat/conversations/{wxfConv}/messages",
            new { clientMsgId = Guid.NewGuid().ToString("N"), messageType = "IMAGE", payload = new { url = "/uploads/ct.png" } }, buyerToken);
        var imgOk = Code(ib) == 0
            && Data(ib).GetProperty("messageType").GetString() == "IMAGE"
            && Data(ib).GetProperty("payload").GetProperty("url").GetString() == "/uploads/ct.png";
        Check("发送图片消息成功", imgOk, $"code={Code(ib)}");
        long imgId = Code(ib) == 0 && Data(ib).TryGetProperty("id", out var ime) ? ime.GetInt64() : 0;

        var (sImgBad, imgbad) = await Call(HttpMethod.Post, $"/api/chat/conversations/{wxfConv}/messages",
            new { messageType = "IMAGE", payload = new { } }, buyerToken);
        Check("图片消息缺少 url 被拒绝(400)", sImgBad == HttpStatusCode.BadRequest || Code(imgbad) == 400);

        var (_, qb3) = await Call(HttpMethod.Post, $"/api/chat/conversations/{wxfConv}/messages",
            new { clientMsgId = Guid.NewGuid().ToString("N"), messageType = "TEXT", content = "回复引用", replyToMessageId = originId }, sellerToken);
        Check("引用回复成功且回带 replyToMessageId",
            Code(qb3) == 0 && Data(qb3).GetProperty("replyToMessageId").GetInt64() == originId);

        var (sReplyBad, rbBad) = await Call(HttpMethod.Post, $"/api/chat/conversations/{wxfConv}/messages",
            new { messageType = "TEXT", content = "非法引用", replyToMessageId = 99999999 }, sellerToken);
        Check("引用不存在消息被拒绝(400)", sReplyBad == HttpStatusCode.BadRequest || Code(rbBad) == 400);

        var (sOtherRecall, orb) = await Call(HttpMethod.Post, $"/api/chat/messages/{originId}/recall", null, sellerToken);
        Check("撤回他人消息返回 403", sOtherRecall == HttpStatusCode.Forbidden, $"code={Code(orb)}");

        var (sRecall, _) = await Call(HttpMethod.Post, $"/api/chat/messages/{originId}/recall", null, buyerToken);
        Check("发送者 2 分钟内撤回成功", sRecall == HttpStatusCode.OK);

        var (_, mlAfter) = await Call(HttpMethod.Get, $"/api/chat/conversations/{wxfConv}/messages", null, sellerToken);
        var recallMasked = Data(mlAfter).TryGetProperty("items", out var arrAfter)
            && arrAfter.EnumerateArray().Any(x => x.GetProperty("id").GetInt64() == originId
                && x.GetProperty("status").GetString() == "RECALLED"
                && (x.GetProperty("content").ValueKind == JsonValueKind.Null));
        Check("已撤回消息内容被脱敏且状态为 RECALLED", recallMasked);

        var (sRecallAgain, org2) = await Call(HttpMethod.Post, $"/api/chat/messages/{originId}/recall", null, buyerToken);
        Check("重复撤回返回 409", sRecallAgain == HttpStatusCode.Conflict, $"code={Code(org2)}");

        var (sRecallMissing, rmb) = await Call(HttpMethod.Post, "/api/chat/messages/99999999/recall", null, buyerToken);
        Check("撤回不存在消息返回 404", sRecallMissing == HttpStatusCode.NotFound, $"code={Code(rmb)}");

        if (imgId > 0)
        {
            var (sDeliv, _) = await Call(HttpMethod.Post, $"/api/chat/conversations/{wxfConv}/delivered",
                new { lastDeliveredMessageId = imgId }, sellerToken);
            var (_, mlPeer) = await Call(HttpMethod.Get, $"/api/chat/conversations/{wxfConv}/messages", null, buyerToken);
            var peerDelivered = Data(mlPeer).TryGetProperty("peerLastDeliveredMessageId", out var pd) && pd.GetInt64() == imgId;
            Check("送达回执更新且对端游标可见", sDeliv == HttpStatusCode.OK && peerDelivered, $"delivered={sDeliv}");
        }

        var (sTyping, _) = await Call(HttpMethod.Post, $"/api/chat/conversations/{wxfConv}/typing", new { typing = true }, buyerToken);
        var (sTypingOff, _) = await Call(HttpMethod.Post, $"/api/chat/conversations/{wxfConv}/typing", new { typing = false }, buyerToken);
        Check("输入状态接口可用", sTyping == HttpStatusCode.OK && sTypingOff == HttpStatusCode.OK);

        foreach (var tk in new[] { buyerToken, sellerToken })
        {
            await Call(HttpMethod.Post, $"/api/chat/conversations/{wxfConv}/read", new { lastReadMessageId = long.MaxValue }, tk);
            await Call(HttpMethod.Delete, $"/api/chat/conversations/{wxfConv}", null, tk);
        }
    }
}

// 11b. 会话设置 (置顶/免打扰/备注名) + 未读筛选
if (sellerToken.Length > 0 && buyerToken.Length > 0)
{
    Console.WriteLine("\n--- 11b. 会话设置 + 未读筛选 ---");
    long setConv = 0;
    var setSrcId = (DateTime.Now.Ticks % 500_000) + 500_000;
    var (_, setCb) = await Call(HttpMethod.Post, "/api/chat/conversations",
        new { peerUserId = 2, sourceType = "SUPPLY", sourceId = setSrcId }, buyerToken);
    if (Code(setCb) == 0 && Data(setCb).TryGetProperty("id", out var setCe)) setConv = setCe.GetInt64();
    Check("设置测试会话创建成功", setConv > 0, $"code={Code(setCb)}");

    if (setConv > 0)
    {
        var (sSet, _) = await Call(HttpMethod.Put, $"/api/chat/conversations/{setConv}/settings",
            new { isPinned = true, isMuted = true, remark = "老张的店" }, buyerToken);
        Check("更新会话设置(置顶/免打扰/备注名)成功", sSet == HttpStatusCode.OK, $"code={sSet}");

        var (_, after) = await Call(HttpMethod.Get, "/api/chat/conversations?page=1&pageSize=100", null, buyerToken);
        var setItem = Data(after).GetProperty("items").EnumerateArray()
            .FirstOrDefault(x => x.GetProperty("id").GetInt64() == setConv);
        var remarkOk = setItem.ValueKind != JsonValueKind.Undefined
            && setItem.GetProperty("isPinned").GetBoolean()
            && setItem.GetProperty("isMuted").GetBoolean()
            && setItem.GetProperty("remark").GetString() == "老张的店";
        Check("会话列表回显置顶/免打扰/备注名", remarkOk);

        var (sLong, longB) = await Call(HttpMethod.Put, $"/api/chat/conversations/{setConv}/settings",
            new { remark = new string('备', 65) }, buyerToken);
        Check("备注名超长被拒绝(400)", sLong == HttpStatusCode.BadRequest || Code(longB) == 400);

        // 未读筛选: 对方发消息 -> 命中 UNREAD, 已读后消失
        await Call(HttpMethod.Post, $"/api/chat/conversations/{setConv}/messages",
            new { clientMsgId = Guid.NewGuid().ToString("N"), messageType = "TEXT", content = "未读筛选测试" }, sellerToken);
        var (_, unreadList) = await Call(HttpMethod.Get, "/api/chat/conversations?filter=UNREAD&page=1&pageSize=100", null, buyerToken);
        var inUnread = Data(unreadList).TryGetProperty("items", out var uArr)
            && uArr.EnumerateArray().Any(x => x.GetProperty("id").GetInt64() == setConv);
        Check("filter=UNREAD 命中存在未读的会话", inUnread);

        await Call(HttpMethod.Post, $"/api/chat/conversations/{setConv}/read", new { lastReadMessageId = long.MaxValue }, buyerToken);
        var (_, unreadList2) = await Call(HttpMethod.Get, "/api/chat/conversations?filter=UNREAD&page=1&pageSize=100", null, buyerToken);
        var stillUnread = Data(unreadList2).TryGetProperty("items", out var uArr2)
            && uArr2.EnumerateArray().Any(x => x.GetProperty("id").GetInt64() == setConv);
        Check("已读后不再出现在 UNREAD 列表", !stillUnread);

        // 空串清除备注
        var (sClear, _) = await Call(HttpMethod.Put, $"/api/chat/conversations/{setConv}/settings",
            new { remark = "" }, buyerToken);
        var (_, after2) = await Call(HttpMethod.Get, "/api/chat/conversations?page=1&pageSize=100", null, buyerToken);
        var clearedItem = Data(after2).GetProperty("items").EnumerateArray()
            .FirstOrDefault(x => x.GetProperty("id").GetInt64() == setConv);
        var clearedOk = sClear == HttpStatusCode.OK
            && clearedItem.ValueKind != JsonValueKind.Undefined
            && clearedItem.GetProperty("remark").ValueKind == JsonValueKind.Null;
        Check("空串清除备注名", clearedOk);

        foreach (var tk in new[] { buyerToken, sellerToken })
        {
            await Call(HttpMethod.Post, $"/api/chat/conversations/{setConv}/read", new { lastReadMessageId = long.MaxValue }, tk);
            await Call(HttpMethod.Delete, $"/api/chat/conversations/{setConv}", null, tk);
        }
    }
}

// 12. 从聊天转订单 + 拉黑管理
if (sellerToken.Length > 0 && buyerToken.Length > 0 && adminToken.Length > 0)
{
    // 找一个属于卖家(用户2)的可交易供应
    long ctSupplyId = 0; decimal unitPrice = 0; string priceUnit = ""; decimal minQty = 0; decimal stock = 0;
    var (_, sp) = await Call(HttpMethod.Get, "/api/supplies?page=1&pageSize=50", null, null);
    if (Data(sp).TryGetProperty("items", out var supArr))
    {
        foreach (var s in supArr.EnumerateArray())
        {
            if (s.GetProperty("publisherId").GetInt64() == 2 && s.GetProperty("status").GetString() == "ACTIVE"
                && s.TryGetProperty("price", out var pr) && pr.ValueKind != JsonValueKind.Null)
            {
                ctSupplyId = s.GetProperty("id").GetInt64();
                unitPrice = pr.GetDecimal();
                priceUnit = s.GetProperty("priceUnit").GetString() ?? "";
                minQty = s.TryGetProperty("minOrderQty", out var mo) && mo.ValueKind != JsonValueKind.Null ? mo.GetDecimal() : 0;
                stock = s.GetProperty("quantity").GetDecimal();
                break;
            }
        }
    }
    Check("找到卖家可交易供应", ctSupplyId > 0, $"ctSupplyId={ctSupplyId}");

    if (ctSupplyId > 0)
    {
        var qty = minQty > 0 ? minQty : (stock >= 10 ? 10 : stock);

        long ctConv = 0;
        var ctSrcId = (DateTime.Now.Ticks % 900_000) + 100_000;
        var (_, ctCb) = await Call(HttpMethod.Post, "/api/chat/conversations",
            new { peerUserId = 2, sourceType = "SUPPLY", sourceId = ctSrcId }, buyerToken);
        if (Code(ctCb) == 0 && Data(ctCb).TryGetProperty("id", out var cte)) ctConv = cte.GetInt64();
        Check("交易会话创建成功", ctConv > 0, $"code={Code(ctCb)}");

        if (ctConv > 0)
        {
            var (_, shareB) = await Call(HttpMethod.Post, $"/api/chat/conversations/{ctConv}/share-product",
                new { supplyId = ctSupplyId }, sellerToken);
            var shareOk = Code(shareB) == 0
                && Data(shareB).GetProperty("messageType").GetString() == "PRODUCT_CARD"
                && Data(shareB).GetProperty("payload").GetProperty("kind").GetString() == "SUPPLY"
                && Data(shareB).GetProperty("bizType").GetString() == "SUPPLY";
            Check("分享商品卡片成功", shareOk, $"code={Code(shareB)}");

            var (sShareBad, sbBad) = await Call(HttpMethod.Post, $"/api/chat/conversations/{ctConv}/share-product",
                new { }, sellerToken);
            Check("分享未指定对象被拒绝(400)", sShareBad == HttpStatusCode.BadRequest || Code(sbBad) == 400);

            var (_, quoteB) = await Call(HttpMethod.Post, $"/api/chat/conversations/{ctConv}/quote",
                new { supplyId = ctSupplyId, price = unitPrice, priceUnit, quantity = qty }, buyerToken);
            long ctQuoteId = Code(quoteB) == 0 && Data(quoteB).TryGetProperty("id", out var cqe) ? cqe.GetInt64() : 0;
            Check("会话内发起报价成功", ctQuoteId > 0 && Data(quoteB).GetProperty("status").GetString() == "PENDING", $"code={Code(quoteB)}");

            var (_, quoteMsg) = await Call(HttpMethod.Get, $"/api/chat/conversations/{ctConv}/messages?limit=50", null, buyerToken);
            var hasQuoteMsg = Data(quoteMsg).TryGetProperty("items", out var qArr)
                && qArr.EnumerateArray().Any(x => x.GetProperty("messageType").GetString() == "QUOTE"
                    && x.GetProperty("bizId").GetInt64() == ctQuoteId);
            Check("报价消息已推送到会话", hasQuoteMsg);

            var (sQuoteBad, qBad) = await Call(HttpMethod.Post, $"/api/chat/conversations/{ctConv}/quote",
                new { supplyId = ctSupplyId, price = 0, priceUnit, quantity = qty }, buyerToken);
            Check("会话内报价单价非法被拒绝(400)", sQuoteBad == HttpStatusCode.BadRequest || Code(qBad) == 400);

            if (ctQuoteId > 0)
            {
                var (sAccept, _) = await Call(HttpMethod.Post, $"/api/quotes/{ctQuoteId}/accept", null, sellerToken);
                Check("卖家接受会话内报价", sAccept == HttpStatusCode.OK, $"code={sAccept}");
            }

            var (_, orderB) = await Call(HttpMethod.Post, $"/api/chat/conversations/{ctConv}/order",
                new { supplyId = ctSupplyId, price = unitPrice, priceUnit, quantity = qty, deliveryMode = "LOGISTICS", addressSnapshot = "集成测试地址" }, buyerToken);
            long ctOrderId = Code(orderB) == 0 && Data(orderB).TryGetProperty("id", out var coe) ? coe.GetInt64() : 0;
            Check("从聊天直接下单成功", ctOrderId > 0 && Data(orderB).GetProperty("status").GetString() == "PENDING_CONFIRM",
                $"code={Code(orderB)} msg={Msg(orderB)}");

            if (ctOrderId > 0)
            {
                var (_, orderMsg) = await Call(HttpMethod.Get, $"/api/chat/conversations/{ctConv}/messages?limit=50", null, sellerToken);
                var hasOrderMsg = Data(orderMsg).TryGetProperty("items", out var oArr)
                    && oArr.EnumerateArray().Any(x => x.GetProperty("messageType").GetString() == "ORDER_CARD"
                        && x.GetProperty("bizId").GetInt64() == ctOrderId);
                Check("订单卡片已推送到会话", hasOrderMsg);
            }

            // 会话内报价协商: 还价 -> 接受
            var (_, nqB) = await Call(HttpMethod.Post, $"/api/chat/conversations/{ctConv}/quote",
                new { supplyId = ctSupplyId, price = 1.29m, priceUnit, quantity = qty }, buyerToken);
            long negQuoteId = Code(nqB) == 0 && Data(nqB).TryGetProperty("id", out var nqe) ? nqe.GetInt64() : 0;
            Check("会话内发起待协商报价", negQuoteId > 0, $"code={Code(nqB)}");

            long counterQuoteId = 0;
            if (negQuoteId > 0)
            {
                var (sCounter, cb) = await Call(HttpMethod.Post, $"/api/chat/conversations/{ctConv}/quotes/{negQuoteId}/counter",
                    new { price = 1.33m, quantity = qty, remark = "还价测试" }, sellerToken);
                counterQuoteId = Code(cb) == 0 && Data(cb).TryGetProperty("id", out var ce) ? ce.GetInt64() : 0;
                Check("卖家会话内还价成功且状态 PENDING", sCounter == HttpStatusCode.OK && counterQuoteId > 0
                    && Data(cb).GetProperty("status").GetString() == "PENDING", $"code={Code(cb)}");

                var (sCounterByBuyer, _) = await Call(HttpMethod.Post, $"/api/chat/conversations/{ctConv}/quotes/{negQuoteId}/counter",
                    new { price = 1.30m }, buyerToken);
                Check("原报价已进入还价态, 重复还价被拒(409)", sCounterByBuyer == HttpStatusCode.Conflict, $"code={sCounterByBuyer}");

                var (sAcceptCounter, acb) = await Call(HttpMethod.Post, $"/api/chat/conversations/{ctConv}/quotes/{counterQuoteId}/accept", null, buyerToken);
                Check("买方会话内接受还价", sAcceptCounter == HttpStatusCode.OK
                    && Data(acb).GetProperty("status").GetString() == "ACCEPTED", $"code={sAcceptCounter}");

                // 跨会话操作防护
                var (_, crossConvBody) = await Call(HttpMethod.Post, "/api/chat/conversations",
                    new { peerUserId = 4 }, buyerToken);
                long otherConv = Code(crossConvBody) == 0 && Data(crossConvBody).TryGetProperty("id", out var oce) ? oce.GetInt64() : 0;
                var (sCross, _) = await Call(HttpMethod.Post, $"/api/chat/conversations/{otherConv}/quotes/{counterQuoteId}/accept", null, buyerToken);
                Check("跨会话操作报价被拒绝(403)", sCross == HttpStatusCode.Forbidden, $"code={sCross}");

                var (_, nqMsg) = await Call(HttpMethod.Get, $"/api/chat/conversations/{ctConv}/messages?limit=50", null, buyerToken);
                var hasCounterMsg = Data(nqMsg).TryGetProperty("items", out var nArr)
                    && nArr.EnumerateArray().Any(x => x.GetProperty("messageType").GetString() == "QUOTE"
                        && x.GetProperty("bizId").GetInt64() == counterQuoteId);
                Check("还价卡片已推送到会话", hasCounterMsg);

                // 会话内拒绝报价
                var (_, rqB) = await Call(HttpMethod.Post, $"/api/chat/conversations/{ctConv}/quote",
                    new { supplyId = ctSupplyId, price = 1.31m, priceUnit, quantity = qty }, buyerToken);
                long rejQuoteId = Code(rqB) == 0 && Data(rqB).TryGetProperty("id", out var rqe) ? rqe.GetInt64() : 0;
                if (rejQuoteId > 0)
                {
                    var (sRej, rjB) = await Call(HttpMethod.Post, $"/api/chat/conversations/{ctConv}/quotes/{rejQuoteId}/reject", null, sellerToken);
                    Check("卖家会话内拒绝报价成功且状态 REJECTED", sRej == HttpStatusCode.OK
                        && Data(rjB).GetProperty("status").GetString() == "REJECTED", $"code={sRej}");
                }
                else Check("卖家会话内拒绝报价成功且状态 REJECTED", false, $"quoteId={rejQuoteId} code={Code(rqB)}");

                if (otherConv > 0)
                    await Call(HttpMethod.Delete, $"/api/chat/conversations/{otherConv}", null, buyerToken);
            }

            // 拉黑管理
            var (sBlock, _) = await Call(HttpMethod.Post, "/api/chat/blocks", new { userId = 2 }, buyerToken);
            var (_, bl) = await Call(HttpMethod.Get, "/api/chat/blocks", null, buyerToken);
            var blockListed = Data(bl).EnumerateArray().Any(x => x.GetProperty("id").GetInt64() == 2);
            Check("拉黑后列表可见", sBlock == HttpStatusCode.OK && blockListed);

            var (sBlockedSend, _) = await Call(HttpMethod.Post, $"/api/chat/conversations/{ctConv}/messages",
                new { messageType = "TEXT", content = "被拉黑后仍尝试发送" }, buyerToken);
            Check("拉黑后发送消息返回 403", sBlockedSend == HttpStatusCode.Forbidden);

            var (sBlockedConv, _) = await Call(HttpMethod.Post, "/api/chat/conversations",
                new { peerUserId = 2, sourceType = "DEMAND", sourceId = 1 }, buyerToken);
            Check("拉黑后无法再发起会话", sBlockedConv == HttpStatusCode.Forbidden, $"code={sBlockedConv}");

            var (sUnblock, _) = await Call(HttpMethod.Delete, "/api/chat/blocks/2", null, buyerToken);
            var (_, bl2) = await Call(HttpMethod.Get, "/api/chat/blocks", null, buyerToken);
            var cleared = Data(bl2).GetArrayLength() == 0;
            Check("取消拉黑后列表清空", sUnblock == HttpStatusCode.OK && cleared);

            var (sAfterUnblock, _) = await Call(HttpMethod.Post, $"/api/chat/conversations/{ctConv}/messages",
                new { clientMsgId = Guid.NewGuid().ToString("N"), messageType = "TEXT", content = "取消拉黑后恢复" }, buyerToken);
            Check("取消拉黑后恢复正常发送", sAfterUnblock == HttpStatusCode.OK);

            var (sUnblockNone, unb) = await Call(HttpMethod.Delete, "/api/chat/blocks/2", null, buyerToken);
            Check("重复取消拉黑返回 404", sUnblockNone == HttpStatusCode.NotFound, $"code={Code(unb)}");

            var (sSelfBlock, _) = await Call(HttpMethod.Post, "/api/chat/blocks", new { userId = 3 }, buyerToken);
            Check("不能拉黑自己(400)", sSelfBlock == HttpStatusCode.BadRequest);

            foreach (var tk in new[] { buyerToken, sellerToken })
            {
                await Call(HttpMethod.Post, $"/api/chat/conversations/{ctConv}/read", new { lastReadMessageId = long.MaxValue }, tk);
                await Call(HttpMethod.Delete, $"/api/chat/conversations/{ctConv}", null, tk);
            }
        }
    }
}

// 13. 数据大屏 + AI 接口
if (adminToken.Length > 0 && buyerToken.Length > 0)
{
    Console.WriteLine("\n--- 13. 数据大屏 + AI ---");
    {
        var (sOv, ov) = await Call(HttpMethod.Get, "/api/admin/dashboard/overview", null, adminToken);
        var ovOk = sOv == HttpStatusCode.OK
            && Data(ov).TryGetProperty("users", out var uv) && uv.GetInt64() > 0
            && Data(ov).TryGetProperty("dealAmount", out _);
        Check("大屏总览返回正常", ovOk, $"code={Code(ov)}");

        var (sTrend, tr) = await Call(HttpMethod.Get, "/api/admin/dashboard/trend?range=7d", null, adminToken);
        var trOk = sTrend == HttpStatusCode.OK && Data(tr).GetArrayLength() == 7
            && Data(tr)[0].TryGetProperty("date", out _);
        Check("大屏成交趋势返回 7 天序列", trOk, $"code={Code(tr)}");

        var (sCat, cat) = await Call(HttpMethod.Get, "/api/admin/dashboard/category", null, adminToken);
        Check("大屏品类分布返回正常", sCat == HttpStatusCode.OK && Data(cat).GetArrayLength() > 0);

        var (sReg, reg) = await Call(HttpMethod.Get, "/api/admin/dashboard/region", null, adminToken);
        var regOk = sReg == HttpStatusCode.OK
            && Data(reg).EnumerateArray().Any(x => x.GetProperty("townName").GetString() == "谯楼街道");
        Check("大屏地区分布含息县乡镇", regOk, $"code={Code(reg)}");

        var (sRole, role) = await Call(HttpMethod.Get, "/api/admin/dashboard/users", null, adminToken);
        Check("大屏用户结构返回正常", sRole == HttpStatusCode.OK && Data(role).GetArrayLength() > 0);

        var (sChat, ca) = await Call(HttpMethod.Get, "/api/admin/dashboard/chat?range=7d", null, adminToken);
        Check("大屏消息活跃返回 7 天序列", sChat == HttpStatusCode.OK && Data(ca).GetArrayLength() == 7);

        var (sForbid, _) = await Call(HttpMethod.Get, "/api/admin/dashboard/overview", null, buyerToken);
        Check("非管理员访问大屏被拒绝(403)", sForbid == HttpStatusCode.Forbidden);

        // AI 未配置 LLM -> 501 (不伪造)
        var (sAiChat, aiChat) = await Call(HttpMethod.Post, "/api/ai/chat", new { message = "你好" }, buyerToken);
        Check("AI 客服未配置返回 501", sAiChat == HttpStatusCode.NotImplemented, $"code={Code(aiChat)}");

        var (sAiAssist, aiAssist) = await Call(HttpMethod.Post, "/api/ai/assist/publish", new { type = "SUPPLY", title = "小麦" }, buyerToken);
        Check("AI 发布辅助未配置返回 501", sAiAssist == HttpStatusCode.NotImplemented, $"code={Code(aiAssist)}");

        var (sAiInsight, _) = await Call(HttpMethod.Post, "/api/ai/insight", null, adminToken);
        Check("AI 大屏解读未配置返回 501", sAiInsight == HttpStatusCode.NotImplemented);

        var (sAiAskForbid, _) = await Call(HttpMethod.Post, "/api/ai/ask", new { question = "平台有多少用户" }, buyerToken);
        Check("非管理员访问数据问答被拒绝(403)", sAiAskForbid == HttpStatusCode.Forbidden);

        var (sAiAsk, _) = await Call(HttpMethod.Post, "/api/ai/ask", new { question = "平台有多少用户" }, adminToken);
        Check("AI 数据问答未配置返回 501", sAiAsk == HttpStatusCode.NotImplemented);

        var (sAiBadType, _) = await Call(HttpMethod.Post, "/api/ai/assist/publish", new { type = "XXX" }, buyerToken);
        Check("AI 发布辅助非法类型被拒绝(400)", sAiBadType == HttpStatusCode.BadRequest);

        // AI 对话持久化 (会话列表 / 归属校验 / 未配置不落库)
        var (sAiList, aiList) = await Call(HttpMethod.Get, "/api/ai/conversations", null, buyerToken);
        var aiTotal0 = Data(aiList).TryGetProperty("total", out var at0) ? at0.GetInt64() : -1;
        Check("AI 会话列表可访问且返回数组", sAiList == HttpStatusCode.OK && Code(aiList) == 0
            && Data(aiList).TryGetProperty("items", out _), $"code={Code(aiList)}");

        var (sAiListAfter, aiListAfter) = await Call(HttpMethod.Get, "/api/ai/conversations", null, buyerToken);
        var aiTotal1 = Data(aiListAfter).TryGetProperty("total", out var at1) ? at1.GetInt64() : -2;
        Check("AI 未配置(501)时对话不落库", aiTotal0 == aiTotal1, $"before={aiTotal0} after={aiTotal1}");

        var (sAiMsg404, m404) = await Call(HttpMethod.Get, "/api/ai/conversations/99999999/messages", null, buyerToken);
        Check("访问不存在 AI 会话消息返回 404", sAiMsg404 == HttpStatusCode.NotFound || Code(m404) == 404, $"code={Code(m404)}");

        var (sAiDel404, d404) = await Call(HttpMethod.Delete, "/api/ai/conversations/99999999", null, buyerToken);
        Check("删除不存在 AI 会话返回 404", sAiDel404 == HttpStatusCode.NotFound || Code(d404) == 404, $"code={Code(d404)}");

        var (sAiRename404, r404) = await Call(HttpMethod.Put, "/api/ai/conversations/99999999", new { title = "改名" }, buyerToken);
        Check("重命名不存在 AI 会话返回 404", sAiRename404 == HttpStatusCode.NotFound || Code(r404) == 404, $"code={Code(r404)}");

        var (sAiUnauth, _) = await Call(HttpMethod.Get, "/api/ai/conversations");
        Check("未登录访问 AI 会话列表返回 401", sAiUnauth == HttpStatusCode.Unauthorized);
    }
}

// ================= AI E2E: 自管 API 实例 + 内置 Mock LLM =================
// 需要先构建 Api 项目 (dotnet build AgroMarket.Api)。该段会在 18080 端口拉起独立 API 进程，
// 用环境变量注入 Mock LLM 地址, 验证真实工具调用协议 (role=tool 必须带 tool_call_id)。
Console.WriteLine("\n--- AI E2E (自管实例 + Mock LLM) ---");

string? apiDll = null;
for (var probe = AppContext.BaseDirectory; probe is not null; probe = Path.GetDirectoryName(probe.TrimEnd(Path.DirectorySeparatorChar)))
{
    var candidate = Path.Combine(probe, "AgroMarket.Api", "bin", "Debug", "net10.0", "AgroMarket.Api.dll");
    if (File.Exists(candidate)) { apiDll = candidate; break; }
}

if (apiDll is null)
{
    Console.WriteLine("  SKIP  AI E2E: 未找到 AgroMarket.Api.dll, 请先构建 Api 项目");
}
else
{
    var mock = new MockLlmServer(19099);
    mock.Start();
    Process? api = null;
    var aiHttp = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:18080"), Timeout = TimeSpan.FromSeconds(30) };
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{apiDll}\"",
            WorkingDirectory = Path.GetDirectoryName(apiDll)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:18080";
        psi.Environment["Ai__BaseUrl"] = "http://127.0.0.1:19099/v1";
        psi.Environment["Ai__ApiKey"] = "test-key";
        psi.Environment["Ai__Model"] = "mock-model";
        api = Process.Start(psi);

        var aiUp = false;
        for (var i = 0; i < 40; i++)
        {
            try { if ((await aiHttp.GetAsync("/health")).IsSuccessStatusCode) { aiUp = true; break; } }
            catch { }
            await Task.Delay(500);
        }
        Check("AI E2E 实例启动成功(18080 + Mock LLM)", aiUp);

        if (aiUp)
        {
            async Task<string> AiLogin(string phone, string pwd)
            {
                var r = await aiHttp.PostAsync("/api/auth/login/password",
                    JsonContent.Create(new { phone, password = pwd }));
                var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
                return j.GetProperty("data").GetProperty("token").GetString()!;
            }

            async Task<(HttpStatusCode status, JsonElement body)> AiCall(HttpMethod method, string url, object? payload, string? token)
            {
                var req = new HttpRequestMessage(method, url);
                if (payload != null) req.Content = JsonContent.Create(payload);
                if (token != null) req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                var res = await aiHttp.SendAsync(req);
                var txt = await res.Content.ReadAsStringAsync();
                JsonElement body = default;
                try { body = JsonDocument.Parse(txt).RootElement.Clone(); } catch { }
                return (res.StatusCode, body);
            }

            var adminT = await AiLogin("13800000001", "Abc123456");
            var buyerT = await AiLogin("13800000003", "Abc123456");

            // 1) 发布辅助: titleSuggestions 字段解析
            var (assistStatus, assistBody) = await AiCall(HttpMethod.Post, "/api/ai/assist/publish", new { type = "SUPPLY", title = "小麦" }, adminT);
            var titlesOk = Data(assistBody).ValueKind == JsonValueKind.Object && Data(assistBody).TryGetProperty("titleSuggestions", out var ts)
                && ts.ValueKind == JsonValueKind.Array && ts.GetArrayLength() >= 2;
            Check("AI 发布辅助返回标题建议(titleSuggestions)", assistStatus == HttpStatusCode.OK && titlesOk,
                $"status={(int)assistStatus}");

            // 2) 大屏解读: anomalies 字段解析
            var (insightStatus, insightBody) = await AiCall(HttpMethod.Post, "/api/ai/insight", null, adminT);
            var anomaliesOk = Data(insightBody).ValueKind == JsonValueKind.Object && Data(insightBody).TryGetProperty("anomalies", out var an)
                && an.ValueKind == JsonValueKind.Array && an.GetArrayLength() >= 1;
            Check("AI 大屏解读返回异常提醒(anomalies)", insightStatus == HttpStatusCode.OK && anomaliesOk,
                $"status={(int)insightStatus}");

            // 3) AI 客服: 两轮工具调用往返 (Mock 强校验 role=tool 必须带 tool_call_id, 否则 400)
            var (_, convBefore) = await AiCall(HttpMethod.Get, "/api/ai/conversations", null, buyerT);
            var knownSessions = new HashSet<string>();
            if (Data(convBefore).ValueKind == JsonValueKind.Object && Data(convBefore).TryGetProperty("items", out var cb))
                foreach (var it in cb.EnumerateArray())
                    if (it.TryGetProperty("sessionId", out var sid)) knownSessions.Add(sid.GetString() ?? "");

            var (chatStatus, chatBody) = await AiCall(HttpMethod.Post, "/api/ai/chat", new { message = "平台现在有多少注册用户" }, buyerT);
            var chatAnswer = Data(chatBody).ValueKind == JsonValueKind.Object && Data(chatBody).TryGetProperty("reply", out var rep) ? rep.GetString() : null;
            var chatOk = chatStatus == HttpStatusCode.OK && chatAnswer != null && chatAnswer.Contains("平台");
            Check("AI 客服完成两轮工具调用并回答", chatOk, $"status={(int)chatStatus} answer={chatAnswer}");

            // 清理: 删除本次产生的会话
            var (_, convAfter) = await AiCall(HttpMethod.Get, "/api/ai/conversations", null, buyerT);
            if (Data(convAfter).ValueKind == JsonValueKind.Object && Data(convAfter).TryGetProperty("items", out var ca))
                foreach (var it in ca.EnumerateArray())
                {
                    var sid2 = it.TryGetProperty("sessionId", out var s2) ? s2.GetString() : null;
                    if (sid2 != null && !knownSessions.Contains(sid2) && it.TryGetProperty("id", out var cid))
                        await AiCall(HttpMethod.Delete, "/api/ai/conversations/" + cid.GetInt64(), null, buyerT);
                }

            // 4) 数据问答 (管理员): 同样走工具调用协议
            var (askStatus, askBody) = await AiCall(HttpMethod.Post, "/api/ai/ask", new { question = "平台有多少用户" }, adminT);
            var askAnswer = Data(askBody).ValueKind == JsonValueKind.Object && Data(askBody).TryGetProperty("answer", out var ans) ? ans.GetString() : null;
            Check("AI 数据问答完成工具调用并回答", askStatus == HttpStatusCode.OK && askAnswer != null && askAnswer.Contains("平台"),
                $"status={(int)askStatus} answer={askAnswer}");

            // 5) Mock 校验: 若 API 漏传 tool_call_id, Mock 已返回 400 -> 上面的 chat/ask 必然失败
            Check("Mock LLM 收到过合法的 tool 结果消息", mock.SawValidToolMessage);
        }
    }
    catch (Exception ex)
    {
        Check("AI E2E 流程", false, ex.Message);
    }
    finally
    {
        aiHttp.Dispose();
        if (api is { HasExited: false }) { try { api!.Kill(entireProcessTree: true); } catch { } }
        api?.Dispose();
        mock.Stop();
    }
}

// 13. 节点联动 (数据上报 / 双向同步 / 脱敏)
Console.WriteLine("\n== 13. 节点联动 ==");
try
{
    // 13.1 节点接口必须携带节点令牌
    var (pingNo, _) = await Call(HttpMethod.Get, "/api/federation/node/ping");
    Check("节点接口缺少令牌返回 401", pingNo == HttpStatusCode.Unauthorized, $"status={(int)pingNo}");
    var (pingBad, _) = await Call(HttpMethod.Get, "/api/federation/node/ping", null, "not-a-jwt");
    Check("节点接口非法令牌返回 401", pingBad == HttpStatusCode.Unauthorized, $"status={(int)pingBad}");

    // 13.2 联动管理仅管理员可读
    var (selfS, selfB) = await Call(HttpMethod.Get, "/api/admin/federation/self", null, adminToken);
    var selfCode = Data(selfB).TryGetProperty("nodeCode", out var nc) ? nc.GetString() : null;
    var selfEnabled = Data(selfB).TryGetProperty("enabled", out var en) && en.GetBoolean();
    Check("管理员可读取联动本节点信息", selfS == HttpStatusCode.OK && Code(selfB) == 0 && selfEnabled && selfCode == "node-xixian",
        $"status={(int)selfS} nodeCode={selfCode}");
    var (selfForbidden, _) = await Call(HttpMethod.Get, "/api/admin/federation/self", null, buyerToken);
    Check("普通用户访问联动管理返回 403", selfForbidden == HttpStatusCode.Forbidden, $"status={(int)selfForbidden}");

    // 13.3 签发节点令牌, 可作为节点身份访问节点接口
    var (tokS, tokB) = await Call(HttpMethod.Post, "/api/admin/federation/nodes/token", new { nodeCode = "node-xixian" }, adminToken);
    var nodeTok = Data(tokB).TryGetProperty("token", out var tk) ? tk.GetString() : null;
    Check("管理员签发节点令牌(JWT)", tokS == HttpStatusCode.OK && !string.IsNullOrEmpty(nodeTok) && nodeTok!.Split('.').Length == 3,
        $"len={nodeTok?.Length}");
    var (pingOk, pingOkB) = await Call(HttpMethod.Get, "/api/federation/node/ping", null, nodeTok);
    var peerCode = Data(pingOkB).TryGetProperty("peer", out var pc) ? pc.GetString() : null;
    Check("合法节点令牌访问节点接口并回带身份", pingOk == HttpStatusCode.OK && peerCode == "node-xixian", $"peer={peerCode}");

    // 13.4 上报接收 + 幂等 + 身份一致性
    var batch = "itest-" + Guid.NewGuid().ToString("N")[..10];
    object ReportPayload(string nodeCode, int orders) => new
    {
        nodeCode,
        batchNo = batch,
        periodDate = DateTime.Today.ToString("yyyy-MM-dd"),
        generatedAt = DateTime.Now,
        metrics = new[]
        {
            new
            {
                statDate = "2099-12-31", userCount = 0, activeUserCount = 0, newUserCount = 0,
                supplyCount = 0, demandCount = 0, orderCount = orders, tradeAmount = 12.5m,
                messageCount = 0, conversationCount = 0,
            },
        },
        records = new[]
        {
            new
            {
                type = "ORDER", externalId = "itest-order-fixture", regionCode = "411528",
                occurredAt = "2099-12-31T10:00:00",
                data = new { amount = 12.5m, buyerRef = "ref_abc123", sellerRef = "ref_def456" },
            },
        },
    };
    var (rec1, rec1B) = await Call(HttpMethod.Post, "/api/federation/node/reports", ReportPayload("node-xixian", 7), nodeTok);
    Check("节点上报接收成功", rec1 == HttpStatusCode.OK && Code(rec1B) == 0, $"status={(int)rec1}");
    var (rec2, _) = await Call(HttpMethod.Post, "/api/federation/node/reports", ReportPayload("node-xixian", 7), nodeTok);
    Check("重复上报同一批次仍成功(幂等)", rec2 == HttpStatusCode.OK, $"status={(int)rec2}");
    var (recBad, recBadB) = await Call(HttpMethod.Post, "/api/federation/node/reports", ReportPayload("node-evil", 1), nodeTok);
    Check("上报节点与令牌身份不一致返回 403", recBad == HttpStatusCode.Forbidden || Code(recBadB) == 403, $"status={(int)recBad}");

    var (mS, mB) = await Call(HttpMethod.Get, "/api/admin/federation/metrics?source=node-xixian&from=2099-12-31&to=2099-12-31", null, adminToken);
    var metrics2099 = Data(mB);
    var metricOk = mS == HttpStatusCode.OK && metrics2099.ValueKind == JsonValueKind.Array
        && metrics2099.GetArrayLength() == 1 && metrics2099[0].GetProperty("orderCount").GetInt64() == 7;
    Check("重复上报后按来源+日期去重且指标不累加", metricOk,
        $"count={(metrics2099.ValueKind == JsonValueKind.Array ? metrics2099.GetArrayLength() : -1)}");

    var (rS, rB) = await Call(HttpMethod.Get, "/api/admin/federation/records?source=node-xixian&days=7&limit=200", null, adminToken);
    var recs = Data(rB);
    var fixtureCount = 0;
    if (recs.ValueKind == JsonValueKind.Array)
        foreach (var r in recs.EnumerateArray())
            if (r.TryGetProperty("externalId", out var eid) && eid.GetString() == "itest-order-fixture") fixtureCount++;
    Check("重复上报后按来源+类型+外部ID去重(仅 1 条)", fixtureCount == 1, $"count={fixtureCount}");

    // 13.5 脱敏: 发布一条标题含手机号的供应 -> 上架 -> 推送 -> 落库标题须脱敏
    long catId = 0, countyId = 0, townId = 0;
    var (_, sl) = await Call(HttpMethod.Get, "/api/supplies?pageSize=1", null, sellerToken);
    if (Data(sl).TryGetProperty("items", out var its) && its.ValueKind == JsonValueKind.Array && its.GetArrayLength() > 0)
    {
        var it = its[0];
        if (it.TryGetProperty("countyId", out var cid) && cid.ValueKind == JsonValueKind.Number) countyId = cid.GetInt64();
        if (it.TryGetProperty("townId", out var tid) && tid.ValueKind == JsonValueKind.Number) townId = tid.GetInt64();
    }
    var (_, catB) = await Call(HttpMethod.Get, "/api/categories", null, sellerToken);
    if (Data(catB).ValueKind == JsonValueKind.Array)
        foreach (var c in Data(catB).EnumerateArray())
            if (c.TryGetProperty("parentId", out var pid) && pid.ValueKind == JsonValueKind.Number) { catId = c.GetProperty("id").GetInt64(); break; }

    long maskSupplyId = 0;
    if (catId > 0 && countyId > 0 && townId > 0)
    {
        var (cs, csB) = await Call(HttpMethod.Post, "/api/supplies", new
        {
            title = "联动脱敏13800138000小麦", categoryId = catId, description = (string?)null,
            quantity = 10m, unit = "斤", price = 1.2m, priceUnit = "元/斤", negotiable = true,
            minOrderQty = (decimal?)null, countyId, townId, villageId = (long?)null,
            spec = (string?)null, harvestTime = (string?)null, deliveryMode = "BOTH",
            contactPhone = (string?)null, tradeNote = (string?)null, effectiveDays = 30, images = (List<string>?)null,
        }, sellerToken);
        maskSupplyId = Data(csB).TryGetProperty("id", out var sid) ? sid.GetInt64() : 0;
        if (maskSupplyId > 0)
            await Call(HttpMethod.Post, $"/api/admin/supplies/{maskSupplyId}/audit/true", null, adminToken);
        Check("创建并上架含手机号的测试供应", cs == HttpStatusCode.OK && maskSupplyId > 0, $"id={maskSupplyId}");
    }
    else Check("创建并上架含手机号的测试供应", false, "缺少分类或区域数据");

    // 13.6 注册对端 + 推送 + 拉取
    var (peerReg, peerB) = await Call(HttpMethod.Post, "/api/admin/federation/nodes", new
    {
        nodeCode = "node-itest-peer", nodeName = "集成测试对端", level = "COUNTY",
        regionCode = "410100", baseUrl = baseUrl, nodeToken = (string?)null,
        direction = "BOTH", status = "ACTIVE",
    }, adminToken);
    var peerId = Data(peerB).TryGetProperty("id", out var pidv) ? pidv.GetInt64() : 0;
    Check("注册对端节点成功", peerReg == HttpStatusCode.OK && peerId > 0, $"id={peerId}");

    var (pushS, pushB) = await Call(HttpMethod.Post, "/api/admin/federation/sync/push?node=node-itest-peer", null, adminToken);
    var pushOk = pushS == HttpStatusCode.OK && Data(pushB).TryGetProperty("succeeded", out var ps) && ps.GetInt32() == 1;
    Check("推送本节点数据到对端成功", pushOk, $"body={Msg(pushB)}");

    var (pullS, pullB) = await Call(HttpMethod.Post, "/api/admin/federation/sync/pull?node=node-itest-peer", null, adminToken);
    var pullOk = pullS == HttpStatusCode.OK && Data(pullB).TryGetProperty("succeeded", out var qs) && qs.GetInt32() == 1;
    Check("从对端增量拉取成功", pullOk, $"body={Msg(pullB)}");

    // 13.7 上报审计 / 汇聚
    var (repS, repB) = await Call(HttpMethod.Get, "/api/admin/federation/reports?source=node-xixian", null, adminToken);
    var repTotal = Data(repB).TryGetProperty("total", out var rt) ? rt.GetInt64() : -1;
    Check("上报批次可审计", repS == HttpStatusCode.OK && repTotal >= 1, $"total={repTotal}");
    var (aggS, aggB) = await Call(HttpMethod.Get, "/api/admin/federation/aggregate?range=7d", null, adminToken);
    var aggNodes = Data(aggB).TryGetProperty("nodeCount", out var an) ? an.GetInt32() : -1;
    Check("跨节点汇聚返回结果", aggS == HttpStatusCode.OK && Code(aggB) == 0 && aggNodes >= 1, $"nodeCount={aggNodes}");
    var aggHasSelf = Data(aggB).TryGetProperty("byNode", out var aggByNode) && aggByNode.ValueKind == JsonValueKind.Array
        && aggByNode.EnumerateArray().Any(x => x.TryGetProperty("sourceNodeCode", out var sc) && sc.GetString() == "node-xixian");
    Check("跨节点汇聚默认纳入本节点(IncludeSelfInAggregate)", aggHasSelf, "byNode 缺少本节点 node-xixian");
    // 区划前缀过滤: 本节点 411528(息县) 命中, 对端 410100(郑州) 不命中
    var (aggOwnS, aggOwnB) = await Call(HttpMethod.Get, "/api/admin/federation/aggregate?range=7d&regionPrefix=411528", null, adminToken);
    var ownNodes = Data(aggOwnB).TryGetProperty("byNode", out var obn) && obn.ValueKind == JsonValueKind.Array
        ? obn.EnumerateArray().Select(x => x.GetProperty("sourceNodeCode").GetString()).ToList() : new List<string?>();
    Check("汇聚按区划前缀过滤(命中本节点)", aggOwnS == HttpStatusCode.OK
        && ownNodes.Contains("node-xixian") && !ownNodes.Contains("node-itest-peer"),
        $"byNode=[{string.Join(",", ownNodes)}]");
    var (aggNoneS, aggNoneB) = await Call(HttpMethod.Get, "/api/admin/federation/aggregate?range=7d&regionPrefix=999999", null, adminToken);
    var noneCount = Data(aggNoneB).TryGetProperty("nodeCount", out var ncn) ? ncn.GetInt32() : -1;
    Check("汇聚按区划前缀过滤(无匹配为空)", aggNoneS == HttpStatusCode.OK && noneCount == 0, $"nodeCount={noneCount}");

    // 13.8 脱敏校验: 落库明细标题已去手机号, 全部明细无原始手机号
    var (mrS, mrB) = await Call(HttpMethod.Get, "/api/admin/federation/records?source=node-xixian&type=SUPPLY&days=7&limit=200", null, adminToken);
    var maskedTitle = "";
    var anyRawPhone = false;
    if (Data(mrB).ValueKind == JsonValueKind.Array)
        foreach (var r in Data(mrB).EnumerateArray())
        {
            var raw = r.TryGetProperty("data", out var dd) ? dd.GetRawText() : "";
            if (raw.Contains("13800138000")) anyRawPhone = true;
            if (r.TryGetProperty("data", out var dd2) && dd2.TryGetProperty("title", out var ttl))
            {
                var ts = ttl.GetString() ?? "";
                if (ts.Contains("联动脱敏")) { maskedTitle = ts; if (raw.Contains("13800138000")) anyRawPhone = true; }
            }
        }
    Check("推送的供应标题已脱敏(手机号被打码)", maskedTitle.Contains("联动脱敏") && maskedTitle.Contains("****") && !maskedTitle.Contains("13800138000"),
        $"title={maskedTitle}");
    Check("落库业务明细不含原始手机号", !anyRawPhone);

    // 清理: 移除测试对端节点
    if (peerId > 0) await Call(HttpMethod.Delete, $"/api/admin/federation/nodes/{peerId}", null, adminToken);
    if (maskSupplyId > 0) await Call(HttpMethod.Post, $"/api/supplies/{maskSupplyId}/status/off_shelf", null, sellerToken);
}
catch (Exception ex)
{
    Check("节点联动流程", false, ex.Message);
}

Console.WriteLine($"\n结果: {passed} 通过, {failed} 失败");
return failed == 0 ? 0 : 1;
// ---------- 内置 Mock OpenAI 兼容服务 ----------
public class MockLlmServer
{
    private readonly int _port;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public bool SawValidToolMessage { get; private set; }

    public MockLlmServer(int port) => _port = port;

    public void Start()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/v1/chat/completions/");
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => Loop(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); _listener?.Stop(); _listener?.Close(); } catch { }
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener!.GetContextAsync(); }
            catch { break; }
            try
            {
                using var reader = new StreamReader(ctx.Request.InputStream);
                var reqText = await reader.ReadToEndAsync();
                var body = JsonDocument.Parse(reqText).RootElement;
                var hasTools = body.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() > 0;
                var messages = body.GetProperty("messages");
                JsonElement? toolMsg = null;
                foreach (var m in messages.EnumerateArray())
                    if (m.TryGetProperty("role", out var r) && r.GetString() == "tool") toolMsg = m;

                string content;
                if (toolMsg is JsonElement tm)
                {
                    var callIdOk = tm.TryGetProperty("tool_call_id", out var tcid) && !string.IsNullOrWhiteSpace(tcid.GetString());
                    if (!callIdOk)
                    {
                        await Write(ctx, 400, "{\"error\":{\"message\":\"missing tool_call_id\"}}");
                        continue;
                    }
                    SawValidToolMessage = true;
                    content = "根据工具查询结果, 平台注册用户数据如下: 已完成统计。";
                }
                else if (hasTools)
                {
                    content = "";
                    var resp = new
                    {
                        choices = new[]
                        {
                            new
                            {
                                index = 0,
                                message = new
                                {
                                    role = "assistant",
                                    content = "",
                                    tool_calls = new[]
                                    {
                                        new
                                        {
                                            id = "call_mock_1",
                                            type = "function",
                                            function = new { name = "get_platform_stats", arguments = "{}" },
                                        },
                                    },
                                },
                                finish_reason = "tool_calls",
                            },
                        },
                    };
                    await Write(ctx, 200, JsonSerializer.Serialize(resp));
                    continue;
                }
                else
                {
                    var lastUser = "";
                    foreach (var m in messages.EnumerateArray())
                        if (m.TryGetProperty("role", out var r) && r.GetString() == "user")
                            lastUser = m.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                    if (lastUser.Contains("请生成"))
                        content = "{\"description\":\"优质农产品, 欢迎选购。\",\"suggestedCategory\":\"粮食\",\"priceMin\":1.2,\"priceMax\":1.5,\"priceUnit\":\"元/斤\",\"titleSuggestions\":[\"优质小麦 8000斤 出售\",\"新收小麦 整批出让\",\"自产小麦 价格可议\"]}";
                    else
                        content = "{\"summary\":\"平台运行平稳。\",\"highlights\":[\"注册用户持续增长\"],\"anomalies\":[\"今日成交为 0, 需关注\"]}";
                }

                var final = new { choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = "stop" } } };
                await Write(ctx, 200, JsonSerializer.Serialize(final));
            }
            catch
            {
                try { await Write(ctx, 500, "{\"error\":{\"message\":\"mock failure\"}}"); } catch { }
            }
        }
    }

    private static async Task Write(HttpListenerContext ctx, int status, string json)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        var buf = Encoding.UTF8.GetBytes(json);
        await ctx.Response.OutputStream.WriteAsync(buf);
        ctx.Response.Close();
    }
}

