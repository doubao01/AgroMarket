# 息县农贸通（AgroMarket）

县域农产品自由交易平台。面向河南省信阳市息县的农户、合作社、采购商与商户，提供供应发布、采购需求、在线协商报价、订单履约登记与平台运营管理能力。

## 技术栈

| 层 | 技术 |
|----|------|
| 前端 | uni-app（Vue 3 + Vite），一套代码编译为 **H5** 与 **微信小程序** |
| 后端 | .NET 10 / ASP.NET Core Web API，Dapper + MySqlConnector |
| 数据库 | MySQL 8 / MariaDB 10.11+（迁移 SQL 兼容两者；开发环境以 MariaDB 10.11 运行） |
| 鉴权 | 无状态 JWT（自定义轻量 Handler + 授权过滤器） |

## 目录结构

```
.
├── database/                 # 数据库脚本（结构 + 真实区划种子数据 + 初始化脚本）
│   ├── 01_schema.sql
│   ├── 02_seed.sql
│   ├── init-db.sh            # 宿主机按序执行全部迁移
│   ├── docker-init.sh        # 容器首启按序执行全部迁移
│   └── 数据说明.md
├── backend/
│   ├── AgroMarket.Api/       # Web API 主项目
│   ├── AgroMarket.Tests/     # 集成冒烟测试（控制台，无外部依赖）
│   └── README.md
├── frontend/                 # uni-app 前端
├── scripts/                  # 多节点联动引导/演示脚本
│   ├── federation-bootstrap.sh
│   └── federation-demo.sh
├── deploy/                   # 省 / 市 / 县三套单节点交付物（compose + env + README）
├── docker-compose.yml        # 单节点部署（MySQL + API）
└── docker-compose.cluster.yml # 多级节点集群部署（省/市/县）
```

## 快速开始（本地开发）

### 1. 初始化数据库

```bash
cd database
# 创建库、账号，并按序执行 01 结构 / 02 种子 / 03 聊天 / 04 聊天增强 / 05 AI 对话 / 06 聊天维护 / 07 联动 / 08 非对称密钥 / 09 运行时设置 迁移（可重复执行）
bash init-db.sh
```

默认库名 `agro_market`，账号 `agromarket` / `***REMOVED***`。详见 `database/数据说明.md`。

### 2. 启动后端

```bash
cd backend
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:8080 \
dotnet run --project AgroMarket.Api
```

- 开发环境启用 Swagger：http://127.0.0.1:8080/swagger
- 健康检查：http://127.0.0.1:8080/health
- 数据库与 JWT 配置见 `backend/AgroMarket.Api/appsettings.Development.json`

### 3. 启动前端（H5 预览）

```bash
cd frontend
npm install
npm run dev:h5      # 默认 http://localhost:5173 ，已配置 /api 代理到后端 8080
```

### 4. 编译微信小程序

```bash
cd frontend
npm run build:mp-weixin   # 产物在 dist/build/mp-weixin
```

用微信开发者工具导入 `frontend/dist/build/mp-weixin`，并在 `frontend/src/manifest.json` 填写小程序 AppID。小程序端无开发代理，网络请求地址在 `frontend/src/utils/request.js` 中配置。

### 5. 运行集成测试

确保后端已启动后：

```bash
cd backend
dotnet run --project AgroMarket.Tests
```

## 测试账号（开发环境预置）

| 手机号 | 密码 | 角色 |
|--------|------|------|
| 13800000001 | Abc123456 | 平台管理员（ADMIN） |
| 13800000002 | Abc123456 | 农户（FARMER） |
| 13800000003 | Abc123456 | 采购商（BUYER） |
| 13800000004 | Abc123456 | 合作社（COOP） |

开发环境短信验证码为随机码，会打印在后端日志中（`[DEV-SMS]`）。

## 核心业务流程

```
供应/采购发布 → 管理员审核（ACTIVE）→ 详情页「聊一聊」发起会话 / 买方·卖方报价
→ 对方还价/接受 → 买方确认下单（订单快照成交价与商品）
→ 卖方确认订单（待付款）→ 买方发起支付 → 待发货
→ 卖方发货（配送中）→ 买方确认收货（已完成）
```

- 订单状态机在后端强制校验，非法流转直接拒绝。
- 只有交易对手方（非报价发起方）可以接受或拒绝报价。
- 只有买方可以由已接受的报价创建订单，且同一报价不可重复下单。
- 商品成交时对商品名称、规格、单位、价格、数量做快照，后续修改不影响历史订单。
- 未支付订单禁止发货；支付失败或渠道不可用不会改变订单状态。

## 支付渠道（可插拔）

支付通过 `IPaymentProvider` 抽象，渠道与业务解耦：

| 渠道 | Channel | 可用条件 | SDK / 说明 |
| --- | --- | --- | --- |
| 开发模拟 | `MOCK` | Development 且 `Payment:MockEnabled=true` | 内置实现，立即置为已支付，仅用于开发/演示，统计中明确排除 |
| 微信支付 | `WECHAT` | 配置 `Payment:Wechat:*` | 官方推荐 SDK `SKIT.FlurlHttpClient.Wechat.TenpayV3`（APIv3 Native 扫码），未配置时返回 501 |
| 支付宝 | `ALIPAY` | 配置 `Payment:Alipay:*` | 蚂蚁官方 SDK `AlipaySDKNet.Standard`（当面付/扫码预下单），未配置时返回 501 |

- 真实渠道未配置资质与密钥前不可用，发起支付返回 501，不写入任何支付记录，**绝不伪造支付**。
- 配置全部外置（`appsettings*.json` / 环境变量 / `.env`），代码不硬编码任何商户号、密钥或网关地址。
- 接口：`GET /api/payments/channels`、`POST /api/payments`、`GET /api/payments/order/{orderId}`。
- 异步回调：`POST /api/payments/notify/{channel}`（匿名），微信使用 SDK 验签+解密事件，支付宝使用 SDK 验签；回调成功幂等地将订单由 `TO_PAY` 置为 `TO_SHIP`。
- 生产环境禁止开启 `Payment:MockEnabled`（启动时校验）。

## 文件上传（支持断点续传）

- 单文件直传：`POST /api/files/upload`（multipart，字段 `file`）。
- 分片断点续传：`POST /api/files/upload/init` → `POST /api/files/upload/chunk` → `POST /api/files/upload/complete`，`GET /api/files/upload/status?uploadId=` 查询已上传分片。
- 分片大小由 `Storage:ChunkSizeKb` 配置（默认 1024KB）；临时分片存放于 `uploads/.tmp/{uploadId}`，超过 24 小时自动清理。
- 前端 `uploadImageSmart(filePath, size, onProgress)`：大于 2MB 自动走分片续传，否则直传；供应发布页已接入。
- 文件落地本地 `uploads/`，通过 `/uploads` 静态映射访问；仅允许图片类型并做魔数校验。

## 站内聊天

参考 BOSS 直聘式"业务驱动沟通"：从供需详情页可直接与对方发起会话，聊天记录与业务（商品/报价/订单）关联，交易状态仍由后端状态机变更，聊天不改变业务状态。

- 会话模型：一对一（P2P），按 `conv_key = min(uid):max(uid):sourceType:sourceId` 去重；同一对用户同一来源复用同一会话。
- 消息可靠性：先落库再返回；`clientMsgId` 幂等（唯一约束），断线重发不会产生重复消息。
- 未读与已读：按成员维护 `unread_count`，标记已读由服务端校验成员身份后重算；删除会话仅对当前用户隐藏，收到新消息自动恢复显示。
- 会话管理：每成员独立维护置顶（`is_pinned`）、免打扰（`is_muted`）与备注名（`remark`，仅自己可见，留空即清除）；会话列表长按菜单可置顶/免打扰/设置备注名/删除。免打扰会话仍累计未读，但不再弹出新消息提示。
- 权限：所有读写、会话管理均校验 `ChatParticipant` 成员身份；被拉黑后无法发起会话或发消息（`chat_block`）。
- 实时性：原生 WebSocket 通道 `/ws/chat`（`uni.connectSocket` 接入，JSON 文本帧），消息落库后实时推送给会话成员；断线自动重连，未连上时降级为轮询拉取。前端由 `utils/chat-store.js` 在 App 启动后建立**单一常驻连接**，维护全局未读总数并同步底部"消息"tab 角标，收到新消息时跨页提示（会话页仅复用该连接，不重复建连）。
- 微信式富消息：消息撤回（发送者 2 分钟内，撤回后内容脱敏、会话列表显示"[已撤回]"）、引用回复（`replyToMessageId`）、图片消息（`messageType=IMAGE`，`payload.url`）、对方"正在输入"提示、消息状态（发送中/已发送/已送达/已读，依对端游标计算）、时间分组气泡与长按菜单（复制/引用/撤回/拉黑）。
- 从聊天转订单：会话内可直接分享商品卡片（`PRODUCT_CARD`）、发起结构化报价（`QUOTE`，写入 `trade_quote.conversation_id`）、在会话内**还价/接受/拒绝报价**（`.../quotes/{quoteId}/counter|accept|reject`，状态变化实时推送更新卡片）、从聊天直接下单（自动生成已接受报价并创建订单，推送 `ORDER_CARD`）；交易状态仍由后端状态机强制流转。
- 拉黑管理：`POST /blocks`、`DELETE /blocks/{userId}`、`GET /blocks`；任一方向拉黑后，双方无法发起会话或发送消息（`chat_block`）。前端「我的 → 黑名单」可查看并解除拉黑。
- 举报：供应/采购详情页底部与聊天长按菜单（举报对方）提供入口，`POST /api/reports`；管理员在「管理后台 → 举报」标签处理。
- 消息搜索：全局搜索 `GET /search?keyword=`（在用户参与的会话中匹配消息，返回命中会话+消息）与会话内搜索 `GET /search?keyword=&conversationId=`（校验成员）；仅匹配未撤回的文本/卡片类消息。
- 接口前缀 `/api/chat`：`POST /conversations`、`GET /conversations`、`GET /conversations/{id}`、`PUT /conversations/{id}/settings`、`DELETE /conversations/{id}`、`GET /conversations/{id}/messages`、`POST /conversations/{id}/messages`、`POST /conversations/{id}/read`、`POST /conversations/{id}/delivered`、`POST /conversations/{id}/typing`、`POST /conversations/{id}/share-product`、`POST /conversations/{id}/quote`、`POST /conversations/{id}/quotes/{quoteId}/counter|accept|reject`、`POST /conversations/{id}/order`、`POST /messages/{id}/recall`、`GET /unread`、`GET /search`、`GET/POST /blocks`、`DELETE /blocks/{userId}`；实时通道 `GET /ws/chat?token=`。
- 前端：`pages/chat/list.vue`（会话列表，已作为底部"消息"标签，含全局搜索框与长按管理菜单：置顶/免打扰/备注名/删除；未读总数实时同步 tab 角标）、`pages/chat/chat.vue`（聊天窗口，复用全局连接，含报价卡片操作面板与会话内搜索，支持从搜索结果跳转高亮定位消息）；供应/采购详情页提供"聊一聊"入口。

## 数据大屏 + AI 能力

- 数据大屏：管理员访问 `pages/dashboard/index.vue`（`/api/admin/dashboard/*`），展示总览指标、成交趋势、品类供需、乡镇分布、用户结构、消息活跃；真实成交口径与后台统计一致。入口在"管理后台"页。
- AI 能力（OpenAI 兼容 LLM，可对接 DeepSeek/通义/智谱）：
  - 智能客服/农事助手：`pages/ai/assistant.vue`（"我的"页入口），后端 `/api/ai/chat`，可实时查询平台供需与订单（只读工具）；对话历史持久化，支持多会话列表、续聊、重命名与删除（`/api/ai/conversations*`）。
  - 内容发布辅助：供应/采购发布页"AI 帮我写"按钮，`/api/ai/assist/publish` 生成描述/分类/价格区间，仅预填表单。
  - 大屏智能解读：大屏页 `AI 数据解读` 卡片，`/api/ai/insight` 自动生成结论、`/api/ai/ask` 对数据提问。
- **配置**：LLM 通过 `Ai:*`（`BaseUrl`/`Model`/`ApiKey`）配置，Key 可用环境变量 `Ai__ApiKey` 覆盖，代码不硬编码；未配置时 AI 接口返回 `501`，不影响业务。
- 设计文档：`当前工作区/.monkeycode/docs/ai-dashboard-design.md`。

## 多节点联动（数据上报 / 同步）

面向"多个县域 + 省级"扁平节点，提供业务数据上报与双向同步能力（管理员后台 `pages/admin/federation.vue`，后端 `AgroMarket.Api/Federation/`）。

- **拓扑**：扁平多节点（省/市/县对等），各节点通过 `appsettings` 的 `Federation:*` 配置自身身份，在后台登记对端。
- **鉴权**：节点间用 JWT 节点令牌，支持双模式——`SYMMETRIC`（`Federation:SharedKey` HS256 共享密钥，默认）与 `ASYMMETRIC`（RS256 每节点密钥对，对端仅存公钥，单点泄露不可伪造全网令牌）。`issuer/audience=AgroMarketFederation`，与用户登录 JWT 隔离。请求头 `Authorization: Bearer <token>` 或 `X-Node-Token`。
- **上报内容**：按天汇总指标（用户/供需/订单/成交额/消息）+ 脱敏业务明细（供应/需求/真实成交订单）。真实成交口径与数据大屏一致；明细不含手机号等身份信息（`PiiMasker` 脱敏，订单买卖双方为不可逆假名）。
- **同步方式**：手动（后台"立即推送/拉取"）或定时（`Federation:AutoSync` + `SyncIntervalMinutes`，后台 `FederationSyncWorker`；跨副本用 MySQL 咨询锁互斥 + 启动错峰/周期抖动）。推送/拉取均幂等（指标按 `来源+日期`、明细按 `来源+类型+外部ID`、批次按 `来源+批次号`）。
- **汇聚上收（多跳）**：可选开启中间节点转发（`Federation:ForwardReceived`）与汇聚端接收（`AcceptForwarded`），实现 `县→市→省` 多跳；载荷携带原始来源 `sourceNodeCode`，接收端按**原始来源**落库去重，直报与转发不会双计。
- **只读汇聚节点**：`Federation:Role=AGGREGATOR` 时该节点禁业务写接口（放行登录与联动管理），仅用于汇聚/中转。
- **接口**：节点间 `api/federation/node/{ping,reports,export,key}`；管理端 `api/admin/federation/{self,nodes,nodes/token,reports,metrics,records,aggregate,health,settings,sync/push,sync/pull,key,key/generate,nodes/{id}/key/fetch}`（仅管理员）。
- **配置**：`Federation:Enabled` 开启后必须配置 `NodeCode`/`NodeName`（`SYMMETRIC` 下 `SharedKey` ≥32 字节），且 `TokenMode`/`Role` 取值合法；未配置时节点接口不可用，不影响其它业务。
- **运行时可调**：`AutoSync`/`SyncIntervalMinutes`/`BackfillDays`（默认 7）/`RecordWindowHours`/`IncludeSelfInAggregate`/`ForwardReceived`/`AcceptForwarded`/`HealthStaleMinutes`/`NodeName`/`RegionCode` 可在后台「联动 → 设置」热改（持久化 `fed_setting`）。
- **界面**：管理后台"联动"页对端节点按省 / 市 / 县分组展示，并含"设置"Tab（同步健康 + 运行设置 + 密钥管理）；`aggregate` 默认纳入本节点，形成"本节点 + 对端"全景汇聚（可用 `IncludeSelfInAggregate=false` 关闭）。
- **省/市视角**：`aggregate?regionPrefix=` 可按区划码前缀过滤来源节点（省=前 2 位如 `41`，市=前 4 位如 `4101`）；健康总览 `GET api/admin/federation/health` 查看各对端 `healthy/stale/lastError`。
- **部署**：开箱即用的**省 / 市 / 县三套单节点交付物**见 `deploy/`；三级一体编排见 `docker-compose.cluster.yml`（1 省 + 1 市 + 2 县），`federation-bootstrap` 一次性完成"互相登记对端 + 首轮同步"。本机两实例快速演示：`sh scripts/federation-demo.sh`。
- 设计文档：`当前工作区/.monkeycode/docs/federation-design.md`；部署手册：`当前工作区/.monkeycode/docs/federation-deploy.md`。

## 重要说明

- **支付渠道**：微信/支付宝基于各自官方 SDK 实现，配置齐全后自动启用；开发环境默认启用 `MOCK` 模拟支付用于联调。
- **图片上传**：供应发布支持多图上传，小文件直传、大文件分片断点续传（见“文件上传”），文件落地本地 `uploads/`；未接入对象存储。
- **真实成交额统计**：后台统计的“真实成交额”仅计入已完成、非演示、且存在真实支付渠道（`channel` 不属于 `MOCK/OFFLINE/DEMO`）成功记录的订单；开发环境自建订单标记为演示数据单独统计。
- **数据合规**：息县行政区划采用可核实数据，无法核实的编码/村庄以占位或标注方式处理，详见 `database/数据说明.md`。

## 部署

单节点：

```bash
cp .env.example .env   # 修改数据库密码与 JWT 密钥
docker compose up -d --build
```

生产环境务必配置唯一的 `Jwt__SecretKey`（≥32 字节）与数据库强密码，`Sms__DevMode` 保持关闭。

多级节点集群（省 + 市 + 县，各节点独立库与 API）：

```bash
cp .env.cluster.example .env.cluster   # 填 MYSQL_*、JWT_SECRET_KEY、FEDERATION_SHARED_KEY 等
docker compose -f docker-compose.cluster.yml --env-file .env.cluster up -d --build
docker compose -f docker-compose.cluster.yml --env-file .env.cluster logs -f federation-bootstrap
```

- `FEDERATION_SHARED_KEY` 必须**所有节点一致**（≥32 字节）；每节点 `Federation__NodeCode/NodeName/Level/RegionCode` 各不相同。
- 生产建议 `FEDERATION_TOKEN_MODE=ASYMMETRIC`、省级 `Federation__Role=AGGREGATOR`、`FEDERATION_BACKFILL_DAYS=7`。
- 数据库首启自动按序执行 `01`–`09` 全部迁移（含联动表 / 密钥表 / 运行设置表）。
- **开箱即用的单节点交付物**：`deploy/province`（省）、`deploy/city`（市）、`deploy/county`（县），各自 `cp .env.example .env && docker compose up -d --build`。
- 完整流程、扩容与排障见 `当前工作区/.monkeycode/docs/federation-deploy.md`。

## 文档索引

- `backend/README.md`：后端结构、配置项、接口约定、已知问题
- `database/数据说明.md`：区划数据来源、可信度标注与待补充清单
- `当前工作区/.monkeycode/docs/chat-adr.md`：站内聊天模块架构决策（ADR）
- `当前工作区/.monkeycode/docs/ai-dashboard-design.md`：AI 能力接入 + 数据大屏设计文档
- `当前工作区/.monkeycode/docs/federation-design.md`：多节点联动设计文档
- `当前工作区/.monkeycode/docs/federation-deploy.md`：多级节点部署手册
- `/swagger`：在线接口文档（仅开发环境）
