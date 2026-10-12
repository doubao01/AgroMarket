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
  - 数据库脚本按序执行：`database/01_schema.sql`（结构）→ `02_seed.sql`（种子）→ `03_chat.sql`（聊天模块增量迁移，含 6 张 chat_* 表 + trade_quote 扩展列）→ `04_chat_wechat.sql`（微信式增强：chat_message 增 `reply_to_message_id`/`recalled_at`，chat_participant 增 `last_delivered_message_id`）→ `05_ai_chat.sql`（AI 对话持久化：`ai_conversation`/`ai_message`），`database/init-db.sh` 已串联五者且可重复执行。
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
