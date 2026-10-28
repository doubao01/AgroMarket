# AgroMarket 后端（.NET 10 Web API）

息县农贸通后端服务。ASP.NET Core Web API + Dapper + MySqlConnector，无状态 JWT 鉴权。

## 目录

```
AgroMarket.Api/
├── Program.cs                 # 启动、DI、JWT、CORS、Swagger、全局过滤器、静态文件
├── Common/
│   ├── ApiResponse.cs         # 统一响应 { code, msg, data }
│   ├── BizException.cs        # 业务异常与错误码
│   ├── AuthHandler.cs         # JWT 校验 Handler + RequireAuth/RequireAdmin + 授权过滤器
│   └── BizExceptionMiddleware.cs
├── Data/DbConnectionFactory.cs
├── Dtos/Dtos.cs               # 全部请求/响应 DTO
├── Services/                  # Region/Auth/Supply/Demand/Trade/User/Message/Favorite/Address/Admin/Dashboard/Storage
├── Chat/                      # 站内聊天模块（逻辑分层）
│   ├── Domain/ChatConstants.cs        # 消息类型/来源类型/筛选常量
│   ├── Dtos/ChatDtos.cs               # 聊天请求/响应 DTO
│   ├── Infrastructure/ChatRepository.cs # Dapper 数据访问（事务、幂等）
│   ├── Application/ChatService.cs     # 用例编排（成员校验、会话去重、消息幂等）
│   ├── Application/ChatTradeService.cs # 会话内交易桥接（分享商品/报价/下单 → 推送卡片消息）
│   └── Api/ChatController.cs          # /api/chat 接口
├── Ai/                        # AI 能力模块（逻辑分层）
│   ├── Domain/ChatMessage.cs          # 对话消息/工具定义/LLM 响应
│   ├── Infrastructure/               # AiOptions / ILlmClient / OpenAiCompatClient
│   ├── Application/                  # AiChatService / AiAssistService / AiInsightService
│   ├── Tools/                        # IAiTool / AiToolRegistry / 内置只读工具
│   └── Api/AiController.cs           # /api/ai 接口
├── Federation/                # 多节点联动模块（数据上报 / 同步，逻辑分层）
│   ├── Dtos/FederationDtos.cs         # 节点/批次/指标/明细/汇聚 DTO
│   ├── Infrastructure/FederationOptions.cs   # Federation:* 配置
│   ├── Infrastructure/FederationRepository.cs # Dapper 数据访问（幂等 upsert）
│   ├── Infrastructure/FedHttpClient.cs        # 对端 HTTP 客户端（推/拉）
│   ├── Application/FederationTokenService.cs  # 节点 JWT 签发/校验
│   ├── Application/FederationSnapshotBuilder.cs # 本地指标+脱敏明细生成
│   ├── Application/FederationService.cs       # 接收/推送/拉取/导出
│   ├── Application/FederationAdminService.cs  # 管理端查询/汇聚/节点管理
│   ├── Application/FederationSyncWorker.cs    # 后台定时同步
│   └── Api/                   # RequireNode 过滤器 + 节点/管理端控制器
└── Controllers/               # Auth/Region/Market/Trade/Support/Admin/Dashboard/Payment/PaymentNotify
```

## 运行

```bash
# 依赖: .NET 10 SDK；数据库需先按 ../database/init-db.sh 初始化
cd backend
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:8080 \
dotnet run --project AgroMarket.Api
```

推荐使用后台方式运行（长驻服务）：

```bash
cd backend && dotnet run --project AgroMarket.Api
```

## 配置项（appsettings.json / 环境变量）

| 配置 | 说明 |
|------|------|
| `ConnectionStrings:Default` | MySQL 连接串 |
| `Jwt:SecretKey` | JWT 密钥，**生产必须 ≥32 字节且唯一** |
| `Jwt:ExpireHours` | Token 有效期（默认 168 小时） |
| `Sms:DevMode` | 是否启用开发短信模式（仅开发环境允许 true） |
| `Payment:MockEnabled` | 开发模拟支付开关（仅开发环境允许 true） |
| `Payment:Wechat:*` | 微信支付商户参数（AppId/MchId/ApiV3Key/CertSerialNo/PrivateKey/NotifyUrl），配置齐全后渠道可用 |
| `Payment:Alipay:*` | 支付宝应用参数（AppId/PrivateKey/AlipayPublicKey/Gateway/NotifyUrl/ReturnUrl），配置齐全后渠道可用 |
| `Storage:LocalRoot` | 本地上传目录（默认 uploads，映射到 /uploads） |
| `Storage:ChunkSizeKb` | 分片上传分片大小 KB（默认 1024） |
| `Federation:Enabled` | 是否启用多节点联动（启用时必须配置下方身份与密钥） |
| `Federation:NodeCode` / `NodeName` / `Level` / `RegionCode` | 本节点身份（`Level`∈PROVINCE/CITY/COUNTY） |
| `Federation:SharedKey` | 节点令牌签名密钥（HS256，`SYMMETRIC` 模式下 **≥32 字节**，全联邦共享） |
| `Federation:TokenMode` | 令牌签名模式：`SYMMETRIC`(默认, HS256) / `ASYMMETRIC`(RS256 每节点密钥对) |
| `Federation:Role` | 节点角色：`BUSINESS`(默认, 业务读写) / `AGGREGATOR`(汇聚只读, 禁业务写) |
| `Federation:ForwardReceived` | 是否把已接收的下级数据向上级转发（多跳 县→市→省，仅中间节点开启） |
| `Federation:AcceptForwarded` | 是否接收"非直接对端来源"的转发数据（仅上级/汇聚节点开启；关闭时来源不一致 403） |
| `Federation:AutoSync` / `SyncIntervalMinutes` | 定时同步开关与周期（分钟）；worker 单实例用 MySQL 咨询锁互斥，另有启动错峰 + 周期抖动 |
| `Federation:BackfillDays` / `RecordWindowHours` / `MaxRecordsPerPush` / `TokenDays` | 上报回补天数（默认 7）/ 明细时间窗 / 单批明细上限 / 令牌有效期 |
| `Federation:IncludeSelfInAggregate` | 跨节点汇聚是否纳入本节点（默认 `true`，形成"本节点+对端"全景） |
| `Federation:HealthStaleMinutes` | 对端同步"过期"判定阈值（分钟）；`0` 自动取 `max(2×间隔, 60)` |
| `CorsOrigins` | 允许的前端来源 |

环境变量使用双下划线映射，例如 `Jwt__SecretKey`、`ConnectionStrings__Default`。

启动时会做安全校验：非开发环境若 JWT 密钥为空/过短/以 `dev-` 开头、连接串为空、开启短信开发模式、或开启模拟支付，将拒绝启动。开启 `Federation:Enabled` 时还会校验节点身份齐备、`TokenMode`/`Role` 取值合法，否则拒绝启动。

## 支付渠道

支付通过可插拔的 `IPaymentProvider` 实现，业务层不感知具体渠道；真实渠道基于各自官方 SDK：

- 微信支付：`SKIT.FlurlHttpClient.Wechat.TenpayV3`（APIv3 Native 扫码）。
- 支付宝：`AlipaySDKNet.Standard`（当面付/扫码预下单）。

接口：

- `GET /api/payments/channels`：列出渠道及可用状态。
- `POST /api/payments`：买方对 `TO_PAY` 订单发起支付，成功后订单进入 `TO_SHIP`。
- `GET /api/payments/order/{orderId}`：订单支付记录（买卖双方与管理员可见）。
- `POST /api/payments/notify/{channel}`：渠道异步回调（匿名），使用 SDK 验签/解密后幂等地将订单由 `TO_PAY` 置为 `TO_SHIP`。

未配置的真实渠道（微信/支付宝）可用状态为 false，发起支付返回 `501`，且不会写入任何支付记录。所有商户号、密钥、网关与回调地址均来自配置，代码不硬编码。开发环境启用 `MOCK` 渠道用于联调，其支付记录 `channel=MOCK` 被后台成交额统计明确排除。

## 站内聊天

参考 BOSS 直聘式业务沟通，模块位于 `Chat/`，逻辑分层（Domain/Dtos/Infrastructure/Application/Api）。

- 会话去重：`conv_key = min(uid):max(uid):sourceType:sourceId`，唯一约束 `uk_conv_key` 兜底并发创建（冲突后重查既有会话）。
- 消息幂等：`chat_message(conversation_id, client_msg_id)` 唯一；重复 `clientMsgId` 返回既有消息，不重复计数未读。
- 会话设置：`chat_participant` 每成员独立维护置顶（`is_pinned`）、免打扰（`is_muted`）与备注名（`remark`，仅自己可见，`NULLIF(TRIM(...),'')` 落库即留空清除，最长 64 字）。
- 未读/已读：`chat_participant.unread_count` 按成员维护，`POST .../read` 由服务端校验成员后重算；删除会话仅 `is_hidden=1` 作用于当前用户，收到新消息自动恢复。
- 权限：所有端点校验 `ChatParticipant` 成员身份，`userId` 一律取自 token，不信任客户端传参；`chat_block` 双向拦截。
- 实时通道：`GET /ws/chat`（原生 WebSocket，ADR-1）。握手鉴权用 `?token=`（也兼容 `Authorization`/`X-Token` 头）；消息先落库再推送，协议上行 `ping/send/read/delivered/typing/recall`，下行 `connected/pong/message/read/delivered/typing/recall/ack/error`；连接注册表为单进程内存实现（`ChatConnectionManager`），推送通过 `IRealtimeNotifier` 抽象，多实例部署时替换为 Redis 广播即可。
- 微信式富消息：撤回（发送者 2 分钟内，`chat_message.status=RECALLED` + `recalled_at`，内容在出参中脱敏）、引用回复（`reply_to_message_id`，校验引用消息属同一会话）、图片消息（`IMAGE` + `payload.url`）、送达回执（`chat_participant.last_delivered_message_id`）、输入状态广播（不落库）、对端已读/已送达游标随消息分页返回，用于计算己方消息状态。
- 交易状态不受聊天影响，报价/订单仍由 `TradeService` 状态机变更（见 `docs/chat-adr.md`）。

接口：

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/chat/conversations` | 发起/复用会话（`peerUserId`、`sourceType?`、`sourceId?`） |
| GET | `/api/chat/conversations` | 会话列表（`filter=ALL\|UNREAD`、`keyword`、分页） |
| GET | `/api/chat/conversations/{id}` | 会话详情 |
| PUT | `/api/chat/conversations/{id}/settings` | 置顶/免打扰/备注名（`isPinned`/`isMuted`/`remark`，均为可选，`remark` 空串清除） |
| DELETE | `/api/chat/conversations/{id}` | 删除（隐藏）会话 |
| GET | `/api/chat/conversations/{id}/messages` | 消息列表（游标 `beforeId`、`limit`） |
| POST | `/api/chat/conversations/{id}/messages` | 发送消息（`clientMsgId` 幂等） |
| POST | `/api/chat/conversations/{id}/read` | 标记已读（`lastReadMessageId`） |
| POST | `/api/chat/conversations/{id}/delivered` | 送达回执（`lastDeliveredMessageId`） |
| POST | `/api/chat/conversations/{id}/typing` | 输入状态广播（`typing`） |
| POST | `/api/chat/messages/{id}/recall` | 撤回消息（仅发送者、2 分钟内） |
| POST | `/api/chat/conversations/{id}/share-product` | 分享商品/需求为 PRODUCT_CARD（`supplyId`/`demandId`） |
| POST | `/api/chat/conversations/{id}/quote` | 会话内发起报价（挂 `conversation_id`，推送 QUOTE） |
| POST | `/api/chat/conversations/{id}/quotes/{quoteId}/accept` | 会话内接受报价 |
| POST | `/api/chat/conversations/{id}/quotes/{quoteId}/reject` | 会话内拒绝报价 |
| POST | `/api/chat/conversations/{id}/quotes/{quoteId}/counter` | 会话内还价（生成新报价，复用会话） |
| POST | `/api/chat/conversations/{id}/order` | 从聊天直接下单（生成已接受报价→订单，推送 ORDER_CARD） |
| GET | `/api/chat/unread` | 未读总数 |
| GET | `/api/chat/search` | 消息搜索（`keyword` 必填；`conversationId` 可选，空为全局；`limit`），返回命中会话+消息 |
| GET | `/api/chat/blocks` | 拉黑列表 |
| POST | `/api/chat/blocks` | 拉黑用户（`userId`） |
| DELETE | `/api/chat/blocks/{userId}` | 取消拉黑 |
| WS | `/ws/chat?token=` | 实时通道（上行 `ping`/`send`/`read`/`delivered`/`typing`/`recall`，服务端推 `message`/`read`/`delivered`/`typing`/`recall`/`ack`/`error`） |

## 数据大屏 + AI 能力

### 数据大屏统计（仅管理员）

`DashboardService`（`Services/DashboardService.cs`）提供聚合统计，真实成交口径与 `AdminService` 一致（`status='COMPLETED' AND is_demo=0` 且存在非 MOCK/OFFLINE/DEMO 支付记录）。统计结果做 **30s 内存短缓存**（`IMemoryCache`，管理员校验后再读缓存）。

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/admin/dashboard/overview` | 总览：用户/活跃/供需/成交单数与金额 + 今日/本周 |
| GET | `/api/admin/dashboard/trend?range=7d\|30d\|90d` | 成交趋势（按天补零序列） |
| GET | `/api/admin/dashboard/category` | 品类供需与成交额 |
| GET | `/api/admin/dashboard/region` | 息县乡镇供需与成交额 |
| GET | `/api/admin/dashboard/users` | 用户角色结构 |
| GET | `/api/admin/dashboard/users/trend?range=7d/30d/90d` | 按日新增与活跃用户趋势 |
| GET | `/api/admin/dashboard/chat?range=` | 消息活跃趋势 |

### AI 能力（OpenAI 兼容 LLM）

模块位于 `Ai/`，逻辑分层（Domain/Infrastructure/Application/Tools/Api）。LLM 走标准 `/chat/completions` 协议，可对接 DeepSeek/通义/智谱等；配置见 `Ai:*`（`BaseUrl`/`Model`/`ApiKey`，Key 可用环境变量 `Ai__ApiKey` 覆盖），不在代码中硬编码。未配置 LLM 时相关接口返回 `501`，业务不受影响。

对话历史持久化于 `ai_conversation` / `ai_message`（迁移 `05_ai_chat.sql`）：`sessionId` 为空则新建会话，非空且属于当前用户则续聊（他人生成 `403`）。会话在首轮成功后才落库，LLM 失败（如未配置 `501`）不产生空会话。所有 `/api/ai/*` 端点均要求登录（`[RequireAuth]`）。

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/ai/chat` | 智能客服/农事助手（简化 ReAct，工具轮次 ≤3，持久化上下文记忆 + 频控） |
| GET | `/api/ai/conversations` | 当前用户 AI 会话列表（分页） |
| GET | `/api/ai/conversations/{id}/messages` | 会话消息分页（游标 `beforeId`、`limit`） |
| PUT | `/api/ai/conversations/{id}` | 重命名会话 |
| DELETE | `/api/ai/conversations/{id}` | 删除会话（含消息） |
| POST | `/api/ai/assist/publish` | 内容发布辅助（生成描述/分类/价格区间，仅供表单预填） |
| POST | `/api/ai/insight` | 大屏智能解读（仅管理员） |
| POST | `/api/ai/ask` | 大屏数据问答（仅管理员，数字必须来自工具） |

AI 工具白名单（`Ai/Tools/`，只读）：平台总览、成交趋势、乡镇地区分布、供应搜索、需求搜索、我的订单。工具调用遵循 OpenAI 协议（回传 assistant `tool_calls` 与 tool `tool_call_id`），轮次上限 3。AI 端点均做输入长度上限与按用户频控（`AiRateLimiter`）。严禁任意 SQL、下单/支付等写操作与跨用户隐私数据。设计详见 `docs/ai-dashboard-design.md`。

## 多节点联动（数据上报 / 同步）

面向"多个县域 + 省级"的扁平多节点，提供业务数据上报与双向同步。模块位于 `Federation/`。

- **节点身份**：`FederationTokenService` 双模式签发/校验节点 JWT（`issuer/audience=AgroMarketFederation`），claims 含 `node_code/node_name/node_level/region_code`，与用户登录 JWT 隔离。`SYMMETRIC` 用 `Federation:SharedKey`(HS256)，`ASYMMETRIC` 用每节点 RSA 密钥对(RS256，`FederationKeyStore` + `fed_keypair`/`fed_node.public_key`，对端公钥经 `api/federation/node/key` 分发)。校验端按令牌头 `alg` 自动选密钥；未登记公钥的 RS256 令牌一律拒绝。`[RequireNode]` + `RequireNodeFilter`（全局注册）从 `Authorization: Bearer` 或 `X-Node-Token` 解析节点身份。
- **数据内容**：`FederationSnapshotBuilder` 生成本节点"按天汇总指标 + 脱敏业务明细"。指标口径复用 `DashboardService.RealOrderWhere`（真实成交）；明细仅结构化业务字段，自由文本经 `PiiMasker`（去手机号/身份证/邮箱），订单买卖双方为不可逆假名 `ref_xxx`。
- **同步**：`FederationService` 负责推送/拉取/导出/接收；`FederationSyncWorker` 按生效的 `SyncIntervalMinutes` 定时执行（`AutoSync` 控制，启动错峰 10~40s + 周期抖动 0~20%），并用 `FederationRepository.TryAcquireSyncLockAsync`（MySQL `GET_LOCK`）做跨副本互斥。对端未配置 `node_token` 时自动使用本节点令牌。全部写入幂等：指标 `来源+日期`、明细 `来源+类型+外部ID`、批次 `来源+批次号`。
- **接收校验与多跳**：默认要求上报 `payload.nodeCode` 与令牌身份一致，否则 403。开启中间节点 `ForwardReceived` 后，上行载荷会附带**原始来源** `sourceNodeCode`；汇聚端开启 `AcceptForwarded` 即按原始来源归组落库（幂等），支持 `县→市→省` 多跳且不双计。
- **汇聚只读**：`Federation:Role=AGGREGATOR` 时 `AggregatorReadOnlyMiddleware` 拦截 `/api/*` 写方法（放行 `auth`/`federation`/`admin/federation`），仅用于汇聚/中转节点。
- **运行时设置**：`FederationSettings` 以 `fed_setting` 持久化白名单键（`AutoSync`/`SyncIntervalMinutes`/`BackfillDays`/`RecordWindowHours`/`IncludeSelfInAggregate`/`ForwardReceived`/`AcceptForwarded`/`HealthStaleMinutes`/`NodeName`/`RegionCode`），覆盖配置文件并即时生效；`GET/PUT api/admin/federation/settings` 读写。
- **全景汇聚**：`GET api/admin/federation/aggregate` 默认把本节点自身指标一并纳入（来源=本节点编码），得到"本节点 + 对端"全景；`Federation:IncludeSelfInAggregate=false` 时仅汇总已接收的对端数据。另支持 `?regionPrefix=` 按来源节点区划码前缀过滤（省=前 2 位，市=前 4 位）。
- **同步健康**：`GET api/admin/federation/health` 返回各对端 `healthy/stale/lastError`（阈值 `EffectiveStaleMinutes`）。
- **部署**：每个节点部署一套独立 `{ MySQL + API }`（迁移 SQL 内写死 `USE agro_market`，同一 MySQL 实例不能承载两个节点）。开箱即用的省/市/县单节点交付物见根目录 `deploy/`；三级集群编排见 `docker-compose.cluster.yml`，引导脚本 `scripts/federation-bootstrap.sh`，完整手册见 `docs/federation-deploy.md`。

## 文件上传（断点续传）
- `POST /api/files/upload/init`：初始化分片会话，返回 `uploadId`、`chunkSize`、`totalChunks`。
- `GET /api/files/upload/status?uploadId=`：查询已上传分片，用于断点续传。
- `POST /api/files/upload/chunk`：上传单个分片（multipart：`uploadId`、`index`、`file`）。
- `POST /api/files/upload/complete`：合并分片，返回最终 `url`。

分片大小由 `Storage:ChunkSizeKb` 配置（默认 1024KB）；临时分片存放于 `uploads/.tmp/{uploadId}`，超过 24 小时自动清理；仅允许图片并做魔数校验。

## 鉴权约定

- 请求头 `Authorization: Bearer <token>`（小程序端也兼容 `X-Token`）。
- 标记 `[RequireAuth]` 的端点未登录返回 `401`，角色不符返回 `403`，均为统一 JSON。
- 账号被冻结（`status != ACTIVE`）时，携带旧 token 也会被拒绝。

## 响应格式

```json
{ "code": 0, "msg": "ok", "data": { } }
```

`code` 为 `0` 表示成功；非 0 为错误码（如 400 参数错误、401 未登录、403 无权限、404 不存在、409 冲突、501 未实现）。

## 主要接口

| 模块 | 方法 | 路径 |
|------|------|------|
| 认证 | POST | `/api/auth/sms/send`、`/api/auth/login/password`、`/api/auth/login/code`、`/api/auth/register` |
| 用户 | GET/PUT/POST | `/api/user/me`、`/api/user/profile`、`/api/user/password`、`/api/user/auth/merchant` |
| 区域 | GET | `/api/regions/children?parentId=`（省/市/县/镇/村级联）、`/api/regions/tree` |
| 分类 | GET | `/api/categories` |
| 供应 | GET/POST/PUT | `/api/supplies`、`/api/supplies/{id}`、`/api/supplies/{id}/status/{operation}` |
| 采购 | GET/POST/PUT | `/api/demands`、`/api/demands/{id}`、`/api/demands/{id}/status/{operation}` |
| 报价 | GET/POST | `/api/quotes`、`/api/quotes/{id}`、`/api/quotes/{id}/counter|accept|reject` |
| 订单 | GET/POST | `/api/orders`、`/api/orders/{id}`、`/api/orders/from-quote/{quoteId}`、`/api/orders/{id}/status/{operation}` |
| 收藏 | GET/POST/DELETE | `/api/favorites`、`/api/favorites/{targetType}/{targetId}` |
| 消息 | GET/POST | `/api/messages`、`/api/messages/{id}/read`、`/api/messages/read-all`、`/api/messages/unread-count` |
| 聊天 | GET/POST/PUT/DELETE | `/api/chat/conversations`、`/api/chat/conversations/{id}`、`.../settings`、`.../messages`、`.../read`、`.../delivered`、`.../typing`、`.../share-product`、`.../quote`、`.../order`、`/api/chat/messages/{id}/recall`、`/api/chat/unread`、`/api/chat/search`、`/api/chat/blocks` |
| 地址 | GET/POST/DELETE | `/api/addresses`、`/api/addresses/{id}` |
| AI | GET/POST/PUT/DELETE | `/api/ai/chat`、`/api/ai/conversations`、`/api/ai/conversations/{id}`、`.../messages`、`/api/ai/assist/publish`、`/api/ai/insight`、`/api/ai/ask` |
| 举报/公告/上传 | - | `/api/reports`、`/api/notices`、`/api/files/upload` |
| 管理 | GET/POST | `/api/admin/stats|users|auths|supplies|demands|reports|logs` 及对应 audit/freeze/handle 操作 |
| 联动(节点间) | GET/POST | `/api/federation/node/ping`、`/api/federation/node/reports`、`/api/federation/node/export?since=&limit=`、`/api/federation/node/key`（节点令牌鉴权） |
| 联动(管理端) | GET/POST/PUT/DELETE | `/api/admin/federation/self|nodes|nodes/token|reports|metrics|records|aggregate|health|settings|sync/push|sync/pull|key|key/generate|nodes/{id}/key/fetch`（仅管理员） |

列表查询参数（供应/采购）：`keyword`、`categoryId`、`priceMin/Max`、`townId`、`villageId`、`townIncludeVillages`、`orderBy`、`page`、`pageSize`；`status=PUBLIC`（默认）或 `MINE`（我的，需登录）。

## 集成测试

```bash
# 需先启动后端; AI E2E 段还需先构建 Api (自动在 18080 拉起独立实例 + 内置 Mock LLM)
dotnet build AgroMarket.Api/AgroMarket.Api.csproj
dotnet run --project AgroMarket.Tests
```

覆盖：健康检查、未登录 401、密码登录、管理员统计（区分演示成交额）、越权 403、公开列表、报价→接受→下单→确认→未支付禁止发货→渠道不可用 501→模拟支付→发货→收货完整状态机、采购需求报价转订单、站内聊天（会话去重、消息幂等、未读/已读、非成员 403、删除仅对己生效）、WebSocket 实时通道、微信式富消息（图片/引用/撤回/送达/输入状态/对端游标）、实时推送（`read`/`delivered`/`typing`/`recall` 推送至对端）、会话设置（置顶/免打扰/备注名回显、备注超长 400、空串清除、`filter=UNREAD` 命中与已读后移出）、从聊天转订单（分享商品/会话内报价/直接下单/订单卡片推送/会话内还价·接受·拒绝）、拉黑管理，以及 AI E2E（内置 Mock LLM 强校验 tool_call_id，自动验证两轮工具调用、titleSuggestions、anomalies），与多节点联动（节点令牌鉴权、上报接收与幂等、上报身份不一致 403、推送/拉取、批次审计、跨节点汇聚、明细脱敏、多跳转发、非对称密钥分发与验签、同步健康、运行时设置、以及独立实例的 AGGREGATOR 只读拦截）。

## 数据一致性约定

- 金额使用 `DECIMAL`，多表关键写操作使用事务 + 行锁（`FOR UPDATE`）防并发重复。
- 订单由报价生成时对商品名称/规格/单位/价格/数量做快照。
- Dapper 映射的行类型必须是**属性类**（`{ get; set; }`），不能使用位置 record（`BIGINT` 列会因无参构造缺失而映射失败）。

## 已知限制

- 真实支付渠道（微信/支付宝）已基于官方 SDK 实现，配置商户参数后自动启用；未配置时返回 501，开发环境使用 `MOCK` 渠道联调。
- 图片上传本地存储（支持分片断点续传），未接入对象存储。
- 站内聊天已覆盖 P1–P4：文本/图片消息、实时 WebSocket 通道、撤回/引用/送达/输入状态等微信式富消息、从聊天转订单（分享商品/发起报价/直接下单）与拉黑管理。结构化询价报价消息的高级协商（在聊天内还价/接受）仍复用既有 `/api/quotes` 流程。
- WebSocket 连接表为单进程内存实现，多实例部署需改为 Redis 广播（`IRealtimeNotifier` 已抽象）。
- 未包含限流与验证码风控。
