# User Instruction Memory

This file records user instructions, preferences, and teachings for reference in future interactions.

## Format

### User Instruction Entry
[User Instruction Summary]
- Date: [YYYY-MM-DD]
- Context: [Mentioned scenario or time]
- Instructions:
  - [Content of user teaching or instruction, described line by line]

### Project Knowledge Entry
[Project Knowledge Summary]
- Date: [YYYY-MM-DD]
- Context: Discovered by Agent while performing [specific task description]
- Category: [Operations & Deployment|Build Methods|Testing Methods|Troubleshooting & Debugging|Workflow & Collaboration|Environment Configuration]
- Instructions:
  - [Specific knowledge points, described line by line]

## Deduplication Strategy
- Before adding a new entry, check for similar or identical instructions.
- If a duplicate is found, skip the new entry or merge it with the existing one.
- When merging, update the context or date information.
- This helps avoid redundant entries and keeps the memory file tidy.

## Entries

[Project Knowledge Summary]
- Date: 2026-10-11
- Context: Discovered by Agent while adding NuGet payment SDKs and running backend integration tests
- Category: Build Methods / Testing Methods
- Instructions:
  - 后端无 .sln，构建需指定工程：`dotnet build AgroMarket.Api/AgroMarket.Api.csproj`。
  - 集成测试（`backend/AgroMarket.Tests`）打的是**已运行**的后端 `http://127.0.0.1:8080`（可用参数或 `AGRO_API` 覆盖），不是 WebApplicationFactory。
  - 改后端代码后必须先 `dotnet build` 再 kill 旧进程、`dotnet run --no-build` 重启，测试才会命中新逻辑。

[Project Knowledge Summary]
- Date: 2026-10-11
- Context: Discovered by Agent while restoring AlipaySDKNet.Standard / SKIT.FlurlHttpClient.Wechat.TenpayV3
- Category: Environment Configuration
- Instructions:
  - 本环境访问 `https://api.nuget.org/v3/index.json` 会 302 重定向到 `nuget.azure.cn` 镜像；`curl` 查询包版本时需带 `-L` 跟随重定向。
  - 需要确认第三方 SDK 的确切类型/成员时，可用 `System.Reflection.MetadataLoadContext`（含 `System.Private.CoreLib` 与运行时目录）反射读取 DLL，比猜测 API 更可靠。

[Project Knowledge Summary]
- Date: 2026-10-11
- Context: Discovered by Agent while adding the chat module migration
- Category: Operations & Deployment
- Instructions:
  - 数据库脚本按序执行：`database/01_schema.sql`（结构）→ `02_seed.sql`（种子）→ `03_chat.sql`（聊天模块增量迁移，含 chat_* 表 + trade_quote 扩展列）→ `04_chat_wechat.sql`（微信式增强：chat_message 增 `reply_to_message_id`/`recalled_at`，chat_participant 增 `last_delivered_message_id`）→ `05_ai_chat.sql`（AI 对话持久化：`ai_conversation`/`ai_message`）→ `06_chat_cleanup.sql`（会话备注名 `chat_participant.remark`；DROP 死表 `chat_message_receipt`/`chat_attachment`）→ `07_federation.sql`（多节点联动：`fed_node`/`fed_report`/`fed_daily_metric`/`fed_business_record`/`fed_sync_cursor`），`database/init-db.sh` 已串联七者且可重复执行。
  - 对已在运行的库应用新增迁移：用应用账号 `mysql -uagromarket -pagromarket_dev_2026 agro_market < database/0X_xxx.sql`（脚本用 `IF NOT EXISTS`/`ADD COLUMN IF NOT EXISTS`，幂等）。若需要 DDL 权限，用本地 socket 的 root：`mysql -uroot < ...`（**不要带 `-h127.0.0.1`**，root 走 unix_socket 认证，带 `-h` 会 Access denied）。
  - 本机同时有 `mysql` 与 `mariadb` 两个客户端；本地应用账号 `agromarket` / `agromarket_dev_2026`，root 走本地 socket 免密。
  - 后端启动依赖开发配置：`ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:8080 dotnet run --no-build`（连接串/JWT 在 appsettings.Development.json）。

[Project Knowledge Summary]
- Date: 2026-10-11
- Context: Discovered by Agent while adding the chat realtime WebSocket channel
- Category: Troubleshooting & Debugging / Environment Configuration
- Instructions:
  - H5 端 WebSocket 走开发服务器域名，需在 `frontend/vite.config.js` 的 `server.proxy` 增加 `'/ws'` 且 `ws: true`，否则实时连接失败（会降级为轮询）。
  - 聊天实时通道为 `/ws/chat?token=<JWT>`；握手鉴权用 query token（也兼容 Authorization/X-Token 头）。
  - 验证 WS 可用 Node 22 内置全局 `WebSocket`（无需额外依赖）写脚本，或用 `backend/AgroMarket.Tests`（含 ClientWebSocket 用例）。

[Project Knowledge Summary]
- Date: 2026-10-12
- Context: Discovered by Agent while extending integration tests for the chat module
- Category: Testing Methods
- Instructions:
  - `backend/AgroMarket.Tests/Program.cs` 走的是**持久化真库**，聊天断言读的是全局未读数/会话总数，跨轮次会残留状态。新增聊天相关测试需自清理：用 `ResetChatState(token)` 把该用户全部会话标记已读并隐藏，且新建"送达/游标"类用例时要使用**每次唯一**的 `sourceId`（避免复用会话遗留的 `last_read/last_delivered` 游标导致 `GREATEST` 断言失败）。
  - 登录接口路径为 `POST /api/auth/login/password`（`{phone,password}`），响应 `data.token`。

[Project Knowledge Summary]
- Date: 2026-10-12
- Context: Discovered by Agent while implementing chat-to-order
- Category: Troubleshooting & Debugging
- Instructions:
  - `trade_order.quote_id` 为 `NOT NULL` 且有外键，订单必须来自报价。因此"从聊天直接下单"（`POST /api/chat/conversations/{id}/order`）的实现是：先生成一条 `status='ACCEPTED'` 的 trade_quote（可挂 `conversation_id`），再复用 `TradeService.CreateOrderFromQuoteAsync` 正常下单流程，避免绕过状态机。
  - `trade_quote` 的 `conversation_id` 列为 03_chat.sql 增加，报价与会话通过它关联；订单创建后可据该列向会话推送 ORDER_CARD。
  - 商品图片在独立表 `product_image`（`supply_id`/`demand_id`），`product_supply` 无 cover_image 列；需求无 budget 列，价格区间用 `price_min`/`price_max`。

[Project Knowledge Summary]
- Date: 2026-10-12
- Context: Discovered by Agent while implementing AI + dashboard
- Category: Environment Configuration / Troubleshooting & Debugging
- Instructions:
  - LLM 走 OpenAI 兼容 `/chat/completions`，配置节点 `Ai:*`（`BaseUrl`/`Model`/`ApiKey`/`TimeoutSeconds`/`MaxTokens`/`Temperature`）；Key 可用环境变量 `Ai__ApiKey` 覆盖，代码不硬编码。开发环境 `appsettings.Development.json` 默认 `Enabled=true` 但 `ApiKey` 为空，此时 `ILlmClient.IsConfigured=false`，AI 接口返回 501（不伪造结果）。
  - 数据库使用 MySQL `WITH RECURSIVE` 生成日期序列并 LEFT JOIN 业务表来补齐无数据的日期（趋势接口），MariaDB 10.11 兼容。
  - 前端图表采用纯 CSS（view + 百分比高度/宽度）实现，未引入 uCharts/ECharts，H5 与 mp-weixin 行为一致，避免 canvas 适配问题。
  - 大屏真实成交口径与 `AdminService.GetStatsAsync` 一致：`trade_order.status='COMPLETED' AND is_demo=0` 且存在 `payment_record.status='SUCCESS' AND channel NOT IN ('MOCK','OFFLINE','DEMO')`。

[User Instruction Entry]
[工作节奏偏好：批量验证，而非逐个改动验证]
- Date: 2026-10-12
- Context: 用户在大屏投屏主题改动后，对"改一处即构建/验证一处"的方式提出纠正
- Instructions:
  - 不要每完成一个小改动就立刻构建/测试；把同一版本内的改动累积完，最后统一做一次大版本构建验证与测试。
  - 验证/测试放在（该版本）所有改动做完之后进行，过程中不必逐个自测、也不必逐步向用户汇报"已验证"。

[Project Knowledge Summary]
- Date: 2026-10-12
- Context: Discovered by Agent while closing chat-module gaps (global socket / mute / remark / dead tables)
- Category: Workflow & Collaboration / Environment Configuration
- Instructions:
  - 前端聊天连接已统一到 `frontend/src/utils/chat-store.js`：App 启动/登录后建立**单一常驻 WebSocket**，维护全局未读总数并同步底部"消息" tab 角标（`uni.setTabBarBadge`，索引 2），跨页弹提示；会话页 `chat.vue` 改为订阅该 store 事件 + `getSocket().send(...)`，不再自建连接。登录 `afterAuth` 需先 `teardownChat()` 再 `initChat()`（避免复用失效 token 的旧连接）；登出用 `teardownChat()`。
  - 非 H5 平台 WebSocket 地址由 `utils/request.js` 导出的 `BASE_URL` 推导（`BASE_URL.replace(/^http/, 'ws')`），`BASE_URL` 是前后端地址的唯一出处；H5 仍走 `location.host` + vite `/ws` 代理。
  - 会话备注名 `ConversationSettingsRequest.Remark`（空串清除，>64 字 400），存 `chat_participant.remark`（每成员独立、仅自己可见）；会话 DTO 增 `remark`，前端展示 `remark || peer.nickname`。
  - 免打扰（`is_muted`）前端长按菜单可切换；`chat-store` 对免打扰会话不弹新消息提示（未读仍累计）。
  - 死表 `chat_message_receipt`/`chat_attachment` 已全仓库确认无代码引用，从 `03_chat.sql` 移除并在 `06_chat_cleanup.sql` DROP。
  - 前端无 Pinia/事件总线，全局状态用 Vue `reactive` 模块单例（`chat-store.js`）实现即可；H5 与 mp-weixin 构建产物已确认包含新逻辑（如 `replace(/^http/,"ws")`、`设置备注名`）。

[Project Knowledge Summary]
- Date: 2026-10-12
- Context: Discovered by Agent while implementing multi-node federation (data reporting / sync)
- Category: Workflow & Collaboration / Environment Configuration / Troubleshooting & Debugging
- Instructions:
  - 联动模块位于 `backend/AgroMarket.Api/Federation/`（Api/Application/Infrastructure/Dtos 分层，风格同 `Chat/`）：`FederationTokenService`（节点 JWT，issuer/audience 均为 `AgroMarketFederation`，HS256，密钥 `Federation:SharedKey`，claims `node_code/node_name/node_level/region_code`，与用户登录 JWT 完全隔离）；`[RequireNode]` + `RequireNodeFilter`（全局注册）从 `Authorization: Bearer` 或 `X-Node-Token` 解析节点身份；`FederationSnapshotBuilder` 生成本地"汇总指标+脱敏明细"；`FederationService` 负责接收/推送/拉取；`FederationAdminService` 提供管理端查询；`FederationSyncWorker`（BackgroundService）按 `Federation:SyncIntervalMinutes` 定时推+拉。
  - 接口：节点间 `api/federation/node/{ping|reports|export}`（上报接收校验"payload.NodeCode == 令牌身份"，不一致 403）；管理端 `api/admin/federation/{self|nodes|nodes/token|reports|metrics|records|aggregate|sync/push|sync/pull}`（仅 ADMIN）。幂等键：指标 `来源+日期`、明细 `来源+类型+外部ID`、批次 `来源+批次号`。
  - 配置节点 `Federation:*`（`Enabled`/`AutoSync`/`NodeCode`/`NodeName`/`Level`/`RegionCode`/`SharedKey`/`SyncIntervalMinutes`/`BackfillDays`/`RecordWindowHours`/`MaxRecordsPerPush`）。`Program.cs` 校验：`Enabled=true` 时必须有 `NodeCode/NodeName/SharedKey(>=32 字节)`。开发 `appsettings.Development.json`：`Enabled=true, AutoSync=false`（**关闭定时同步以保证集成测试确定性**），节点身份 `node-xixian`。推送时若对端未配置 `node_token` 则自动用 `IssueSelf()` 的本节点令牌。
  - 口径与脱敏：指标复用 `DashboardService.RealOrderWhere`（已由 `private` 改为 `public`，真实成交=COMPLETED+is_demo=0+非 MOCK/OFFLINE/DEMO 支付）；出站自由文本经 `PiiMasker`（去手机号/身份证/邮箱），订单买卖双方压成不可逆假名 `ref_xxx`（以节点编码加盐的 SHA256 前缀），明细无任何自然人身份/联系方式。
  - **MariaDB 坑**：`CAST(@x AS JSON)` 不被支持（语法错误），JSON 列在 MariaDB 是 LONGTEXT 别名，直接以字符串参数插入即可。
  - **Dapper/MySqlConnector 坑**：`DateOnly` 不能作为参数值（`NotSupportedException: The member from of type System.DateOnly cannot be used as a parameter value`），一律用 `DateTime`。
  - 集成测试 `backend/AgroMarket.Tests/Program.cs` 第 13 段覆盖联动（共 143 断言）：节点令牌鉴权、上报幂等、身份不一致 403、推/拉、批次审计、跨节点汇聚、脱敏（先发布标题含手机号的供应并经管理员审核上架→推送→校验落库标题已打码）。**拉/推测试用"自身实例当对端"**（注册 `baseUrl` 指向 `http://127.0.0.1:8080` 的对端节点）即可，无需第二个进程；测试末尾删除该对端节点并下架测试供应。
  - 前端管理后台联动页：`frontend/src/pages/admin/federation.vue`（路由 `pages/admin/federation`，入口在 `pages/admin/index.vue`），API 在 `frontend/src/api/index.js` 的 `federationApi`。

[Project Knowledge Summary]
- Date: 2026-10-12
- Context: Discovered by Agent while designing multi-level (province/city/county) packaging/deployment and a two-instance live demo for federation
- Category: Operations & Deployment / Troubleshooting & Debugging
- Instructions:
  - **部署资产**：`docker-compose.cluster.yml`（多级节点集群，默认 1 省 + 2 县 = 每节点一套独立 `{MySQL + API}`，节点间用网内服务名互访）+ `.env.cluster.example`（集群环境变量模板）。每节点 `db-*` 自带 healthcheck，`api-*` `depends_on` 其 `service_healthy`；`federation-bootstrap` 是一次性引导容器（`restart:"no"`）。
  - **旧 compose 的迁移漏挂 bug（已修）**：`docker-compose.yml` 原先只把 `01_schema.sql`/`02_seed.sql` 挂到 `docker-entrypoint-initdb.d`，全新库会缺 chat/ai/federation 表。现改为挂载 `./database:/migrations:ro` + `./database/docker-init.sh:/docker-entrypoint-initdb.d/00-migrate.sh:ro`，由 `docker-init.sh` 遍历 `/migrations/0*.sql` 依次执行（全量 7 步；迁移后约 31 张表）。`docker-init.sh` 用 `mysql -uroot`（官方镜像），`init-db.sh` 用 `mariadb`（宿主机）。
  - **库名不可改而共享实例**：所有迁移 SQL 内写死 `USE agro_market`，故同一 MySQL 实例上不能跑两个节点；多节点必须各用独立 MySQL 实例（不同容器/端口），靠 `Federation:NodeCode` 区分身份。本机两实例演示用 `sed "s/agro_market/<db>/g"` 生成独立库。
  - **引导脚本** `scripts/federation-bootstrap.sh`：环境变量 `FED_ALL="code|baseUrl|level|name|region;..."`，逐节点登录管理员后把"除自己外"的节点 POST 到 `api/admin/federation/nodes`（`direction=BOTH,status=ACTIVE`，`nodeToken` 留空→用 `IssueSelf()` 本节点令牌），再触发 `sync/pull`+`sync/push`；幂等可重复跑。`baseUrl` 必须是**从执行环境可达**的地址（容器内用服务名，本机用 `127.0.0.1:port`）。
  - **POSIX sh 坑**：`printf '%s' "$X" | tr ';' '\n' | while read` 会**丢掉最后一条**——`printf '%s'` 不带结尾换行，`read` 在 EOF 无分隔符时返回非零，`while read` 直接结束。必须用 `printf '%s\n'`（本次正是因此只处理了 2 个节点中的第 1 个）。外层 while 从管道进入子 shell，`set -e` 下 `[ ] && continue` 的假分支不会触发退出，但换行问题是真因。
  - **aggregate 语义**：`fed_daily_metric` 只存**接收到的对端批次**，本节点自身指标不落该表；因此 `nodeCount` ≠ 参与节点总数，而是"有数据的来源节点数（不含自己）"。演示断言应校验对端 `sourceNodeCode` 出现在 `byNode` 里，而非 `nodeCount>=2`。
  - **本机两实例演示** `scripts/federation-demo.sh`：用 root socket（`mysql -uroot`）重建 `agro_market_demo_a`/`_b`（sed 替换库名跑全量迁移），在 8090/8091 各起一个 `dotnet <dll>`（Development，env 注入 `ConnectionStrings__Default` 与 `Federation__*`），跑 bootstrap 后验证双向汇聚。用法 `sh scripts/federation-demo.sh [run|stop|verify]`。**注意**：dotnet 子进程会随宿主（后台终端）退出被回收；`stop` 只按记录的 PID kill，`pkill -f AgroMarket.Api.dll` 已移除（避免误伤 8080 开发实例）。演示库与 8080 开发实例（`agro_market`）互不影响。
  - 前端联动页对端节点按层级分组：`federation.vue` 的 `nodeGroups` computed（`PROVINCE`/`CITY`/`COUNTY` + 兜底 `OTHER`，空组过滤）+ `.group-title` 样式。
  - 文档：`.monkeycode/docs/federation-design.md`（设计）、`.monkeycode/docs/federation-deploy.md`（部署手册，含配置表/迁移/手工与容器部署/扩容/排障对照表）。

[Project Knowledge Summary]
- Date: 2026-10-12
- Context: Discovered by Agent while adding "include self in panorama aggregate" switch per user request
- Category: Workflow & Collaboration / Environment Configuration
- Instructions:
  - 新配置 `Federation:IncludeSelfInAggregate`（`FederationOptions`，**默认 true**；env `Federation__IncludeSelfInAggregate`）：开启时 `GET api/admin/federation/aggregate` 会把**本节点自身**指标并入结果（`byNode` 增一条 `sourceNodeCode=本节点 NodeCode`，`byDay` 合并本节点按日指标，`nodeCount` 含自己），形成"本节点+对端"全景。
  - 防双计：若 `byNode` 已存在同编码（该节点也曾作为对端上报过自身数据），则**跳过**叠加。
  - `FederationSnapshotBuilder.BuildMetricsAsync` 增加 `(DateTime from, DateTime to)` 重载（闭区间、含无数据日），供 aggregate 复用；原 `BuildMetricsAsync(int days)` 改为委托它。`FederationAdminService` 因此新增 `FederationSnapshotBuilder` 依赖（DI 中已 Scoped 注册）。
  - 集成测试新增断言"跨节点汇聚默认纳入本节点(IncludeSelfInAggregate)"（第 13 段，现 144 断言）；`scripts/federation-demo.sh` 的验证改为断言"全景含本节点与对端两个来源"。
