# 多节点联动（数据上报 / 同步）设计

> 范围：AgroMarket（农贸通）在"多县域 + 省级"场景下的**扁平多节点**业务数据上报与同步能力、
> 可选的**多跳汇聚（县→市→省）**、汇聚只读节点，以及管理后台联动界面。
> 代码位置：`backend/AgroMarket.Api/Federation/`、`frontend/src/pages/admin/federation.vue`、
> `database/{07_federation,08_federation_keys,09_federation_settings}.sql`。

## 1. 目标与非目标

**目标**
- 任一节点（省/市/县实例）可与任意其它节点**对等**地交换：
  - 按天**汇总指标**（用户/供需/订单/成交额/消息）；
  - **脱敏业务明细**（供应、需求、真实成交订单），用于跨节点商机撮合与趋势对比。
- 支持**双向**同步（主动推送 PUSH / 主动拉取 PULL），可手动或定时。
- 弱网/重复投递/网状多路径下**幂等**，不产生脏数据。
- 出站数据**不泄露个人身份**（手机号等），满足跨机构数据共享合规。
- 多级（省/市/县）实例的**打包部署可复现、可校验、不因配置差异出错**。

**非目标**
- 不做中心化网关或行政层级逐级汇总（省节点不替县节点归并数据）。
- 不做跨节点的实时同步（最终一致即可，默认 30 分钟窗口）。
- 不回写业务表：收到的数据只落到只读副本 `fed_*`。

## 2. 拓扑与节点模型

```
            扁平网状（本方案）                      层级树（未采用）
   ┌────────────┐   push/pull   ┌────────────┐
   │ node-xixian │◄────────────►│ node-huaiyang│      县 → 市 → 省
   │  COUNTY     │              │  COUNTY      │      （逐级归并，任一跳断全断）
   └─────┬──────┘              └──────┬───────┘
         │      push/pull              │
         ▼                             ▼
   ┌─────────────────────────────────────────┐
   │            node-henan (PROVINCE)          │
   └─────────────────────────────────────────┘
   每个节点都是一个独立的 AgroMarket 实例（API + 自己的库），彼此对等
```

- **节点 = 一个 AgroMarket 实例**（HTTP 服务 + 独立数据库）。
- `Level ∈ {PROVINCE, CITY, COUNTY}`、`RegionCode` 仅是**标签**，用于展示/筛选与解析令牌，
  **不决定**数据流向，也不构成父子约束。
- 拓扑是**有向图的子图**：每个节点维护一张"对端表"`fed_node`，记录可连通的对端与方向。
- "多级联动"= 把需要交换数据的每个实例都登记为对端。省级节点把各县登记为对端、县级也把省级登记为对端，即形成任意层级间直连。

## 3. 节点身份与令牌

- 全联邦共享一把签名密钥 `Federation:SharedKey`（HS256，≥32 字节），因此**任意节点可校验任意节点的令牌**，无需中心发证。
- 令牌是 JWT，`issuer = audience = AgroMarketFederation`，与用户登录 JWT 完全隔离。
- Claims：`node_code`、`node_name`、`node_level`、`region_code`。
- 校验：`FederationTokenService.Validate`（`Application/FederationTokenService.cs:67`）；
  接口鉴权：`[RequireNode]` + `RequireNodeFilter`，从 `Authorization: Bearer` 或 `X-Node-Token` 取令牌。
- **令牌语义 = "我是谁"**：节点调用对端时，默认携带**本节点自己**的令牌（`IssueSelf()`，`FederationTokenService.cs:63`）。
  对端表里的 `node_token` 可留空；仅当某对端要求"我签发给你"的专属令牌时才填写。
- **双模式（可选）**：`Federation:TokenMode` 取 `SYMMETRIC`（默认，HS256 共享密钥）或 `ASYMMETRIC`（RS256 每节点密钥对）。
  非对称模式下各节点独立私钥（`FederationKeyStore` 单例，持久化 `fed_keypair`），对端仅存公钥（`fed_node.public_key`），
  单点泄露不再可伪造全网令牌。校验端按令牌头 `alg` **自动选密钥**：HS256 用共享密钥，RS256 用对端公钥，
  未登记公钥的 RS256 令牌一律拒绝。公钥经公开接口 `GET /api/federation/node/key` 分发，管理端可 `key/generate` 与 `nodes/{id}/key/fetch`。

## 4. 数据模型（`database/07_federation.sql` + `08` + `09`）

| 表 | 作用 | 幂等键 |
|----|------|--------|
| `fed_node` | 本节点的对端注册表（不含本节点身份，本节点来自配置） | `node_code` |
| `fed_report` | 上报批次审计（原始载荷保留） | `(source_node_code, batch_no)` |
| `fed_daily_metric` | 收到的**按天汇总指标**读模型 | `(source_node_code, stat_date)` |
| `fed_business_record` | 收到的**脱敏业务明细** | `(source_node_code, record_type, external_id)` |
| `fed_sync_cursor` | 主动拉取的增量游标 | `(peer_node_code, cursor_type)` |
| `fed_keypair` | 本节点 RSA 密钥对（`08`，单行） | 单行 |
| `fed_setting` | 运行时设置覆盖（`09`，白名单键值） | `setting_key` |

> 收到的数据 `source_node_code` 一律为**来源节点的真实 nodeCode**（拉取时取导出响应的 `nodeCode`，
> 而非本地登记别名），保证数据归属不受本地别名影响。多跳转发场景下，接收端按载荷中的**原始来源**
> `sourceNodeCode` 归组落库（见 §11）。

## 5. 指标口径与脱敏

**指标口径**（`FederationSnapshotBuilder.BuildMetricsAsync`）复用数据大屏真实成交口径：
`trade_order.status='COMPLETED' AND is_demo=0` 且存在 `payment_record.status='SUCCESS' AND channel NOT IN ('MOCK','OFFLINE','DEMO')`
（常量 `DashboardService.RealOrderWhere`）。每日指标含：`user/activeUser/newUser/supply/demand/order/tradeAmount/message/conversation`。

**脱敏**（`Application/PiiMasker.cs` + 快照构建）
- 出站**自由文本**（如供应标题）经 `PiiMasker` 去除手机号/身份证/邮箱。
- 出站**明细**仅含结构化业务字段（品类/数量/单价/区域编码/区域名等）。
- 订单**买卖双方**以不可逆假名 `ref_xxx`（`SHA256(nodeCode:userId:fed)` 前缀）表示，可跨节点撮合但不可还原到人。
- 不含任何姓名、手机号、地址门牌。

## 6. 协议与接口契约

### 6.1 节点间接口（`/api/federation/node/*`；除公开的 `/key` 外均需节点令牌）

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/ping` | 连通性与身份探测，返回 `{peer, peerName, level}` |
| POST | `/reports` | 接收对端上报 |
| GET | `/export?since=<时间>&limit=<n>` | 增量导出本节点数据（供对端拉取） |
| GET | `/key` | 公开本节点公钥（非对称模式，`{nodeCode, tokenMode, publicKey}`） |

**上报载荷 `FedReportPayload`**
```json
{
  "nodeCode": "node-xixian",
  "batchNo": "node-xixian-20261012184846",
  "periodDate": "2026-10-12",
  "generatedAt": "2026-10-12T18:48:46",
  "metrics": [
    { "statDate": "2026-10-12", "userCount": 0, "activeUserCount": 0, "newUserCount": 0,
      "supplyCount": 0, "demandCount": 0, "orderCount": 3, "tradeAmount": 128.5,
      "messageCount": 0, "conversationCount": 0 }
  ],
  "records": [
    { "type": "SUPPLY", "externalId": "node-xixian-supply-12", "regionCode": "411528",
      "occurredAt": "2026-10-12T10:00:00",
      "data": { "title": "小麦 138****8000", "category": "小麦", "quantity": 100, "unit": "斤",
                "countyName": "息县", "townName": "项店镇" } }
  ]
}
```

**导出响应 `FedExportDto`**：`{ nodeCode, generatedAt, hasMore, nextSince, metrics[], records[] }`
（明细按 `occurredAt` 升序返回；`hasMore=true` 时拉取方以 `nextSince` 作为下一页 `since` 继续拉取，取完后游标推进到 `generatedAt`，避免单批超限导致明细被静默丢弃。）

**接收校验**：默认 `payload.nodeCode` 必须等于令牌身份 `node_code`，否则 `403`；
开启 `Federation:AcceptForwarded` 的汇聚端则允许载荷携带 `sourceNodeCode`（原始来源）的转发数据。
（控制器 `Api/FederationNodeController.cs`。）

### 6.2 管理端接口（`/api/admin/federation/*`，仅管理员）

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/self` | 本节点身份与联动配置状态 |
| GET | `/nodes` | 对端列表 |
| POST | `/nodes` | 新增/更新对端（按 `nodeCode` 幂等 upsert） |
| PUT | `/nodes/{id}` | 修改对端 |
| POST | `/nodes/{id}/status` | 启停（`ACTIVE`/`DISABLED`） |
| DELETE | `/nodes/{id}` | 删除对端（连带清理其游标） |
| POST | `/nodes/token` | 签发节点令牌（`nodeCode` 留空=本节点） |
| GET | `/reports?source=&page=&size=` | 上报批次审计 |
| GET | `/metrics?source=&range=7d\|30d\|90d`（或 `from`/`to`） | 收到的汇总指标 |
| GET | `/records?source=&type=&days=&limit=&offset=` | 收到的业务明细 |
| GET | `/aggregate?range=` | 跨节点汇聚（总量 + 按节点 + 按日），支持 `regionPrefix=` |
| GET | `/health` | 同步健康总览（各对端 `healthy/stale/lastError`） |
| GET | `/settings` | 运行时设置（`{editable, items:[{key,value}]}`） |
| PUT | `/settings` | 更新运行时设置（白名单键，空串=清除覆盖） |
| GET | `/key` | 本节点公钥与令牌模式 |
| POST | `/key/generate?force=` | 生成/重置本节点 RSA 密钥对（`force=true` 才覆盖已有） |
| POST | `/nodes/{id}/key/fetch` | 从对端拉取其公钥（非对称模式） |
| POST | `/sync/push?node=` | 手动推送（`node` 为空=全部 PUSH/BOTH） |
| POST | `/sync/pull?node=` | 手动拉取（`node` 为空=全部 PULL/BOTH） |

## 7. 时序

### 7.1 推送（A → B）
```
A 管理员/定时         A.FederationService      B.FederationNodeController   B.FederationService
    │  push?node=B ─────────►│
    │                        │ 1. 构建快照(指标+脱敏明细)
    │                        │ 2. token = B.node_token ?? IssueSelf()
    │                        │ 3. POST B/api/federation/node/reports  ─────────►│
    │                        │                           校验 payload.nodeCode == 令牌身份
    │                        │                                            4. IngestAsync
    │                        │                               upsert fed_report/metric/record
    │                        │◄──────────── 200 {accept:true}────────────────┤
    │                        │ 5. TouchNodeSync(last_push_at)
    │◄─── {succeeded:1} ─────│
```

### 7.2 拉取（A ← B）
```
A 管理员/定时         A.FederationService        B(/export)              A 本地
    │  pull?node=B ─────────►│
    │                        │ since = fed_sync_cursor[peer=B] ?? now-backfill
    │                        │ GET B/api/federation/node/export?since=&limit= ─►│
    │                        │◄────── {nodeCode:B, generatedAt, metrics, records} ┘
    │                        │ source = 响应.nodeCode (B)
    │                        │ IngestAsync(source=B, batchNo=pull-B-…)
    │                        │ SetCursor(peer=B, generatedAt)
    │◄─── {succeeded:1} ─────│
```

### 7.3 定时（`FederationSyncWorker`）
每（生效的）`SyncIntervalMinutes` 分钟执行一次 `PushAsync(null)` + `PullAsync(null)`；
仅当 `Enabled && AutoSync && HasIdentity` 时运行，启动后**错峰延迟 10~40s** 首跑，每轮再叠加 **0~20% 周期抖动**，避免多节点同时批量推拉。异常只记日志。
跨副本互斥：每轮开始前用 `FederationRepository.TryAcquireSyncLockAsync`（MySQL `GET_LOCK(name,0)` 非阻塞）取锁，取不到则跳过本轮，确保多副本下同一时刻只有一个 worker 在同步。

## 8. 幂等与一致性

- **指标**：`INSERT … ON DUPLICATE KEY UPDATE`（键 `来源+日期`）→ 重复/重叠投递不累加。
- **明细**：键 `来源+类型+外部ID`（`externalId` 由来源节点生成，含节点前缀）→ 重叠不重复。
- **批次**：键 `来源+批次号` → 审计不重复。
- 因此：A↔B 互推、或多条路径重复投递，结果一致；允许任意两节点直连而无需环路检测。
- 拉取按 `hasMore/nextSince` **分页增量**：逐页拉取直至取完，游标推进到对端 `generatedAt`；拉取失败不改游标（下次重试）。

## 9. 管理界面设计

页面 `frontend/src/pages/admin/federation.vue`（路由 `pages/admin/federation`，入口在 `admin/index.vue`）。
结构：**顶部"本节点"卡** + **三 Tab**。

| 区域 | 内容 |
|------|------|
| 本节点卡 | `nodeCode/nodeName/Level/RegionCode`、启用状态、自动同步周期、`立即推送/立即拉取`、身份缺失告警 |
| Tab 对端节点 | 按 **省/市/县分组**展示（`nodeGroups` computed）；每个节点显示方向/状态/令牌/上次推拉/错误；操作 推送/拉取/启停/编辑/删除；新增-编辑表单；令牌签发区 |
| Tab 上报审计 | 上报批次列表（来源/批次号/日期/条数/时间）+ 最近明细（按类型筛选） |
| Tab 跨节点汇聚 | 区间 7d/30d/90d + 参与节点数/成交订单/成交额 + 按节点、按日明细 |

分组仅为**视觉层级**，不改变数据流；即使只登记县级节点，其数据同样直接参与汇聚。

**全景汇聚**：`aggregate` 默认把**本节点自身**按 `Federation:IncludeSelfInAggregate`（默认 `true`）并入结果（来源节点编码 = 本节点 `NodeCode`），得到"本节点 + 对端"的完整全景；置 `false` 时退化为"仅已接收的对端数据"。若库中已存在同编码的接收数据，则不再重复叠加，避免自报+接收双计。

## 10. 扩展点

- 新增指标/明细类型：`FedRecordTypes` 增加常量 + 快照构建内补充查询 + 接收端白名单校验。
- **省/市视角（轻量）**：`GET /aggregate` 支持 `regionPrefix` 区划码前缀过滤（省=前 2 位，市=前 4 位）。来源节点编码→区划码映射 = 已登记对端 `fed_node.region_code` + 本节点 `Federation:RegionCode`；带前缀时**未登记/无区划的来源会被排除**。本节点自身也需匹配前缀才会并入全景。
- 未做**父子归属与逐级 rollup**（保持扁平，省不替县归并）；如需严格树形汇总，仍是展示层基于 `source_node_code` 的后续选项。
- 实时联动：可将 `FederationSyncWorker` 的轮询替换为事件驱动（业务写入后入队触发 push）。

## 11. 进阶能力（汇聚上收 / 只读节点 / 健康 / 运行时设置）

- **多跳转发（县→市→省）**：中间节点开 `Federation:ForwardReceived`，推送（push）与导出（pull 的 `export`）时把**已接收的下级数据**一并以
  `sourceNodeCode=原始来源` 附带（`BuildForward*`）；汇聚端开 `Federation:AcceptForwarded`
  后按原始来源归组落库。由于落库幂等键仍是 `来源+日期` / `来源+类型+外部ID`，**县级直报省级 + 经市级转发不会双计**。
  关闭 `AcceptForwarded` 时，来源与令牌身份不一致一律 `403`（默认，防越权伪造）。
- **汇聚只读节点**：`Federation:Role=AGGREGATOR` 时 `AggregatorReadOnlyMiddleware` 对 `/api/*` 写方法返回 `403`，
  放行 `auth` / `federation` / `admin/federation`，用于仅汇聚/中转的省级节点；默认 `BUSINESS` 不受影响。
- **同步健康**：`FederationAdminService.GetHealthAsync` 依据各对端 `last_push_at/last_pull_at` 与
  `EffectiveStaleMinutes`（`HealthStaleMinutes` 或 `max(2×SyncIntervalMinutes,60)`）判定 `healthy/stale`，并回带 `lastError`。
- **运行时设置**：`FederationSettings` 单例把白名单键（`AutoSync`/`SyncIntervalMinutes`/`BackfillDays`/`RecordWindowHours`/
  `IncludeSelfInAggregate`/`ForwardReceived`/`AcceptForwarded`/`HealthStaleMinutes`/`NodeName`/`RegionCode`）
  持久化到 `fed_setting` 并**覆盖配置文件**，即时生效（读经内存缓存，更新后失效）。身份与密钥类
  （`Enabled`/`NodeCode`/`Level`/`TokenMode`/`SharedKey`/`Role`）不在白名单，需改配置重启。
- **上线推荐值（交付物已预置）**：`BackfillDays=7`（防中断缺数）、`SyncIntervalMinutes=30`（含错峰/抖动）、
  `TokenMode=ASYMMETRIC`（生产建议）、省级 `Role=AGGREGATOR` 且 `AcceptForwarded=true`、市级 `ForwardReceived=true`。
  单节点交付物见 `deploy/{province,city,county}/`。
