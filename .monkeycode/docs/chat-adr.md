# ADR: AgroTrade 站内聊天模块（Chat）

- 状态：已采纳
- 日期：2026-10-11
- 关联：站内聊天 + 商品卡片 + 询价报价 + 订单流转

## 背景

需要在现有 AgroMarket（.NET 10 + Dapper + MySQL/MariaDB，前端 uni-app H5/小程序）之上，
构建"参考 BOSS 直聘直聊模式"的站内聊天，并把聊天作为供需对接与成交的业务入口：
浏览农产品 → 联系供应商 → 即时沟通 → 确认交易条件 → 生成订单 → 履约跟踪。

现状：`user_message` 是单向通知表，不是聊天；`trade_quote` 已有完整报价/还价/转订单状态机；
`merchant_profile` 提供企业认证；`StorageService` 支持分片上传；无任何实时通道。

## 决策

### ADR-1 实时通道：原生 WebSocket + uni.connectSocket（不直接用 SignalR 客户端）

- 后端使用 `app.UseWebSockets()` + 自建 `ChatHub`（JSON 协议），不使用 SignalR 服务端。
- 前端统一通过 `uni.connectSocket` 接入（H5 与 mp-weixin 同一套 API）。
- 原因：SignalR 官方 JS 客户端在 uni-app 小程序端需要 WebSocket 适配层，兼容成本高、坑多；
  原生 WebSocket 在两端均可直接使用，协议可控。
- 预留：通过 `IRealtimeNotifier` 抽象隔离推送实现，后续若需 SignalR/Redis 横向扩展只需替换实现。
- 降级：小程序端若实时不稳定，按 `ChatMessage.id` 游标轮询补拉（接口与实时解耦）。
- 落地状态（2026-10-11）：已实现 `/ws/chat`（`ChatRealtimeHub`）+ `ChatConnectionManager`（单进程内存）+ `ChatRealtimeNotifier`；握手 `?token=` 鉴权；前端 `utils/chat-socket.js`（自动重连 + 心跳），未连上时降级为 4s 轮询。spike 通过（集成测试覆盖连接/心跳/REST→WS 推送/WS 发送 ack）。
- 消息搜索（2026-10-12）：`GET /api/chat/search`，`conversationId` 为空全局搜索（`conversation_id` 上子查询取每会话最新命中），非空会话内搜索（先校验成员）；仅匹配未撤回的 `TEXT/PRODUCT_CARD/QUOTE/ORDER_CARD`，`content LIKE`。前端会话列表全局搜索框 + 聊天页会话内搜索，搜索结果可跳转并高亮定位消息。

### ADR-2 询价/报价建模：扩展现有 `trade_quote`，不新建 `TradeInquiry`

- 复用 `trade_quote`（含 `supply_id/demand_id/buyer/seller/initiator/price/quantity/status/parent_quote_id/order_id`）。
- 新增 `conversation_id`、`message_id` 以及交付条件字段（`delivery_address/deliver_by/freight_bearer/valid_until`）。
- 原因：避免两套报价模型并存；"询价"即第一条 `PENDING` 报价，还价即 `parent_quote_id` 链。
- 边界：**聊天中的报价消息不产生交易效力**，只有后端 `TradeService` 状态流转才改变业务状态。

### ADR-3 模块落位：同工程内 Chat 模块逻辑分层，不拆独立部署

```
AgroMarket.Api/Chat/
  Domain/          领域模型与枚举（实体、值对象、状态）
  Application/     用例编排（ChatService）
  Infrastructure/  数据访问（ChatRepository，Dapper）
  Api/             REST 控制器（ChatController）
  Realtime/        WebSocket Hub 与推送（后续阶段）
```
- 命名空间 `AgroMarket.Api.Chat.*`，通过 DI 注册，保持与现有 `Services/Controllers` 并存。
- 原因：满足"模块化 + 后期微服务可拆"的要求，同时不引入分布式复杂度。

### 其它约定

- 事务：Dapper + 每请求连接，服务内使用 `conn.BeginTransaction()`（不引入 UnitOfWork 抽象，见风险）。
- 消息可靠性：**先落库再推送**；`ChatMessage(conversation_id, client_msg_id)` 唯一约束保证幂等。
- 未读：`ChatParticipant.unread_count` 按成员维护，已读回执由服务端校验成员身份后更新。
- 权限：所有读写、加入实时频道前校验 `ChatParticipant` 成员身份，服务端从 token 解析 `userId`，**不信任客户端传入的 userId**。
- 身份识别：复用现有 `RequireAuth` / `HttpContext.User().Id`。

## 数据模型（新增 6 表 + 扩展 1 表）

- `chat_conversation`：会话，`conv_key` 唯一去重（`min(uid):max(uid):sourceType:sourceId`）。
- `chat_participant`：成员，各自 `last_read_message_id/unread_count/is_pinned/is_muted/is_hidden`。
- `chat_message`：消息，`message_type` 区分文本/图片/文件/商品卡片/询价/报价/订单卡片/系统。
- `chat_message_receipt`：送达/已读回执。
- `chat_attachment`：图片与附件。
- `chat_block`：拉黑关系。
- `trade_quote` 扩展：`conversation_id/message_id/delivery_address/deliver_by/freight_bearer/valid_until`。

## 影响

- 前端 `pages/message/index.vue`（通知列表）保留；新增 `pages/chat/*` 会话与聊天页。
- `user_message` 继续承担离线通知；聊天实时消息走新通道。
- 新增迁移脚本 `database/03_chat.sql`，`init-db.sh` 增加执行步骤。

## 风险与缓解

- 小程序实时稳定性 → ADR-1 降级轮询；先做技术验证。
- 会话并发创建重复 → DB 唯一约束 `uk_conv_key` 兜底 + 冲突重试。
- 交易一致性 → 报价/订单状态只由后端状态机变更（ADR-2 边界）。
- 范围蔓延 → P1 只做文本会话/消息/未读；图片、卡片、询价报价、在线状态延后。
