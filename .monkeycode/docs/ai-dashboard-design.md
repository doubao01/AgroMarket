# 设计文档：AI 能力接入 + 数据大屏

- 状态：待评审
- 日期：2026-10-12
- 关联：智能客服/农事助手、内容发布辅助、大屏数据智能解读、H5 管理大屏
- 参考：ToyAgent 智能体设计模式（提示词工程、ReAct、工具调用、记忆、意图路由、RAG）

## 一、背景与现状

平台当前状态（已核实）：

- 后端：.NET 10 + ASP.NET Core Web API + Dapper + MySqlConnector（非 EF Core），无任何 AI/LLM 基础设施，未注册 `IHttpClientFactory`。
- 统计：仅 `GET /api/admin/stats`（`AdminController.cs:122` / `AdminService.cs:215`），返回 4 个计数 + 成交额 + 乡镇供需表（`StatsDto`）。
- 前端：uni-app（Vue3 + Vite），22 个页面，`pages/admin/index.vue` 是审核工作台（非大屏），无图表库。
- 数据：`trade_order` / `trade_order_item` / `trade_quote` / `product_supply` / `purchase_demand` / `sys_user` / `payment_record` / `sys_region` / `chat_message` 均含时间、金额、状态、地区字段，SQL 层面足以支撑大屏，**无需改表**。

结论：AI 与大屏均从零起步，但底层数据完备。

## 二、目标与范围

| 模块 | 目标 | 优先级 |
|---|---|---|
| AI 基础设施 | OpenAI 兼容 LLM 客户端抽象 + 配置 + 降级 | P0（其它 AI 能力前置） |
| 智能客服/农事助手 | 聊天会话内 AI 助手，解答种植/行情/平台问题，可查平台数据 | P1 |
| 内容发布辅助 | 发布供应/需求时 AI 生成描述、推荐价格区间、建议分类 | P2 |
| 大屏数据智能解读 | 对大屏统计做只读自然语言问答 + 自动生成分析结论 | P2 |
| H5 管理大屏 | uni-app 内新增图表大屏页 + 扩展统计端点 | P1 |

非目标（本期不做）：语音/多模态、向量数据库 RAG、多智能体协作、独立大屏工程、微信小程序端大屏（H5 优先）。

## 三、总体架构

```
                 ┌──────────────────────────────────────┐
                 │              Frontend (uni-app)        │
                 │  大屏页 dashboard/index.vue (uCharts)  │
                 │  聊天内 AI 助手 / 发布页 AI 建议按钮    │
                 └───────────────┬──────────────────────┘
                                 │ HTTPS /api/ai/*  /api/admin/dashboard/*
                 ┌───────────────▼──────────────────────┐
                 │            AgroMarket.Api              │
                 │  Ai/ 模块（新增）                       │
                 │   ├─ Domain     消息/角色/工具契约        │
                 │   ├─ Infrastructure  OpenAiCompatClient │
                 │   ├─ Application   AiChatService /      │
                 │   │                AiAssistService /     │
                 │   │                AiInsightService       │
                 │   ├─ Tools       AI 可调用的平台数据工具   │
                 │   └─ Api         AiController            │
                 │  Admin/DashboardService（扩展统计）       │
                 └───────────────┬──────────────────────┘
                                 │
        ┌────────────────────────┼─────────────────────────┐
        │                        │                          │
   OpenAI 兼容 LLM          MySQL(Dapper)              内存缓存
   (DeepSeek/通义/智谱)      业务数据                    (统计短缓存)
```

设计原则：

1. **AI 是表现层增强，不改业务状态**：AI 生成的报价/下单建议一律需要用户手动确认走既有接口。
2. **可用性降级**：未配置 LLM 时相关接口返回 501（与支付一致），业务功能不受影响。
3. **Key 隔离**：LLM Key 来自用户项目配置（`USER_LLM_*` / `Ai:Llm:*`），不从 Agent 执行环境读取，代码不硬编码。
4. **工具调用白名单**：AI 只能调用只读/受控工具，不暴露任意 SQL、不下单、不支付。

## 四、AI 基础设施

### 4.1 配置（`appsettings*.json`）

```jsonc
{
  "Ai": {
    "Enabled": true,
    "Provider": "OpenAICompat",
    "BaseUrl": "https://api.deepseek.com/v1",
    "Model": "deepseek-chat",
    "ApiKey": "",                 // 由部署方填入，或用环境变量 Ai__ApiKey
    "TimeoutSeconds": 60,
    "MaxTokens": 1024,
    "Temperature": 0.3
  }
}
```

- 支持环境变量覆盖：`Ai__ApiKey`（.NET 约定双下划线）。
- 用户提供的文档变量名建议：`USER_LLM_BASE_URL` / `USER_LLM_API_KEY` / `USER_LLM_MODEL`，映射进 `Ai:*`。
- 非 Development 环境下若 `Ai:Enabled=true` 且 `ApiKey` 为空 → 启动即失败（类似 JWT/支付校验），避免线上静默不可用。

### 4.2 客户端抽象

```
Ai/Domain/
  ChatMessage(role, content)、ChatRole(system/user/assistant/tool)
  ToolDefinition(name, description, parametersJsonSchema)
  ToolCall(id, name, argumentsJson)
  LlmResponse(content, toolCalls, usage)
Ai/Infrastructure/
  ILlmClient
    Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools, CancellationToken ct)
  OpenAiCompatClient : ILlmClient   // POST {BaseUrl}/chat/completions
```

- 用 `AddHttpClient<ILlmClient, OpenAiCompatClient>()` 注册，天然支持超时/重试/连接池。
- 请求体遵循 OpenAI `/chat/completions`：`model`、`messages`、`tools`、`tool_choice`、`temperature`、`max_tokens`。
- 失败策略：超时/5xx → `BizException(ErrorCodes.ServerError)`；未配置 → `ErrorCodes.NotImplemented(501)`。

### 4.3 对话循环（简化 ReAct，不含自治多轮）

```
1. 组装 system prompt（角色 + 可用工具 + 平台术语）
2. 追加历史消息（记忆，见 4.4）
3. 调用 LLM
4. 若返回 tool_calls：执行白名单工具 → 结果作为 role=tool 消息追加 → 回到 3（最多 N=3 轮，防循环）
5. 否则返回 content 给用户
```

- 设硬上限（工具轮次 ≤3、总消息 token 估算截断），防止无限循环与费用失控。

### 4.4 记忆与上下文

- 智能客服：会话历史取该用户与本 AI 最近 K 条（默认 10）对话，存于内存/或后续落库（本期用短 TTL 内存字典即可，单进程）。
- 上下文压缩：超出 token 预算时从最早消息裁剪，保留 system + 最近 N 条。
- 不做向量检索（RAG 非目标）；平台数据通过工具实时查询。

## 五、AI 能力设计

### 5.1 智能客服 / 农事助手

- 入口：`POST /api/ai/chat`，`{ message, conversationId?, historyId? }` → `{ reply, toolCalls[]?, usage }`。
- system prompt：告知身份（息县农产品平台助手）、职责（平台使用、供需对接、农事常识、行情解释）、限制（不承诺交易、不代下单、不编造价格）。
- 工具（白名单，见 5.4）。
- 前端：`pages/chat/chat.vue` 增加"AI 助手"虚拟会话入口，或独立 `pages/ai/assistant.vue`（推荐独立页，避免与真实用户会话混淆）。
- 频控：每用户每分钟 ≤N 次（内存计数器），防滥用。

### 5.2 内容发布辅助

- 入口：`POST /api/ai/assist/publish`，`{ type: "SUPPLY"|"DEMAND", title?, category?, keywords?, quantity?, priceHint? }`
  → `{ description, suggestedCategory, priceRange{min,max,unit}, titleSuggestions[] }`。
- 前端：`supply/create.vue` / `demand/create.vue` 加"AI 帮我写"按钮，一键填入描述/分类/价格区间（用户可改）。
- **AI 建议不自动提交**，仅填充表单。

### 5.3 大屏数据智能解读

- 入口：`POST /api/ai/insight`，`{}` → `{ summary, highlights[], anomalies[] }`（基于当前统计快照生成自然语言结论）。
- 入口：`POST /api/ai/ask`，`{ question }` → `{ answer, facts[] }`（对大屏指标的只读问答，工具返回聚合数字，LLM 只做表述）。
- **防幻觉约束**：所有数字必须来自工具返回，prompt 明确"只能用工具结果，不得推测"。
- 权限：仅 ADMIN。

### 5.4 AI 工具白名单（只读）

| 工具名 | 说明 | 数据来源 |
|---|---|---|
| `get_platform_stats` | 平台总览（用户/供需/成交额/订单） | `AdminService.GetStatsAsync` |
| `get_trade_trend` | 成交趋势（按天/周/月） | 新增 `DashboardService` |
| `get_region_distribution` | 乡镇/品类分布 | 新增 `DashboardService` |
| `search_supplies` | 按关键词/分类查在售供应（公开信息） | `SupplyService` |
| `search_demands` | 查在售采购需求 | `DemandService` |
| `get_my_orders` | 当前用户订单概览（需鉴权上下文） | `TradeService` |

- 工具注册表：`Ai/Tools/AiToolRegistry.cs`，`ToolDefinition` + 委托实现，集中注册/发现。
- **严禁**：任意 SQL、写操作（下单/支付/改状态）、跨用户隐私数据。

## 六、大屏设计

### 6.1 后端统计扩展

新增 `DashboardService`（`Services/DashboardService.cs`）与端点 `GET /api/admin/dashboard/{tab}`（ADMIN）：

| 分区 | 端点 | 返回 |
|---|---|---|
| 总览 | `/api/admin/dashboard/overview` | 复用扩展 `StatsDto`：+ 今日/本周成交额、活跃用户、在线供给数 |
| 成交趋势 | `/api/admin/dashboard/trend?range=30d` | `[{ date, orderCount, dealAmount }]`（按天聚合 `trade_order`，排除 `is_demo=1`） |
| 品类分布 | `/api/admin/dashboard/category` | `[{ categoryName, supplyCount, demandCount, dealAmount }]` |
| 地区分布 | `/api/admin/dashboard/region` | `[{ townId, townName, supplyCount, demandCount, dealAmount }]`（`sys_region` level=4 and parent_id=countyId） |
| 用户结构 | `/api/admin/dashboard/users` | `[{ role, count }]` + 近 30 天新增趋势 |
| 消息活跃 | `/api/admin/dashboard/chat` | 近 30 天消息量趋势、活跃会话数 |

- 真实成交判定沿用 `AdminService` 现有口径（`status='COMPLETED' AND is_demo=0` 且存在非 MOCK/OFFLINE/DEMO 支付记录）。
- 短缓存：同一统计 30s 内存缓存（`IMemoryCache`）降低重复聚合压力。

### 6.2 前端大屏页

- 路由：`pages/dashboard/index.vue`，从 admin 工作台或"我的"进入；`pages.json` 注册（不占 tabBar，避免普通用户看到）。
- 图表库：**uCharts**（`@qiun/ucharts`，uni-app 生态，H5/小程序通用，canvas 渲染）。
- 布局（移动优先 + 大屏自适应）：
  - 顶部：核心指标卡（用户/供需/成交额/订单）
  - 趋势折线/柱状（成交额、订单量）
  - 品类占比（饼图/环图）
  - 乡镇分布（横向条形图，前 N）
  - 用户结构（环形）
  - 消息活跃（折线）
  - 「AI 解读」卡片：调用 `/api/ai/insight`，展示自动结论 + 支持输入问题
- 数据刷新：下拉刷新 + 定时轮询（30s），或复用现有轮询模式。
- 鉴权：`adminApi` + token；非 ADMIN 直接隐藏入口。

## 七、目录结构（新增）

```
backend/AgroMarket.Api/
  Ai/
    Domain/            ChatMessage / ChatRole / ToolDefinition / ToolCall / LlmResponse
    Infrastructure/    ILlmClient / OpenAiCompatClient / AiOptions
    Application/       AiChatService / AiAssistService / AiInsightService / AiPromptTemplates
    Tools/             AiToolRegistry / 各工具实现
    Api/               AiController
  Services/DashboardService.cs
  Dtos/                新增 Ai*Dtos / Dashboard*Dtos

frontend/src/
  pages/dashboard/index.vue         管理大屏
  pages/ai/assistant.vue            AI 助手（可选）
  api/index.js                      新增 aiApi / dashboardApi（adminApi 扩展）
  components/                       （如需）图表/指标卡组件
```

## 八、安全与合规

- LLM Key 仅存项目配置，不入库、不返回前端、不写入日志。
- 所有 AI 端点：
  - 需登录（已有全局 `RequireAuthFilter`）；
  - 大屏/解读/问答需 `[RequireAdmin]`；
  - 频控 + 输入长度上限（如 ≤2000 字），防费用滥用。
- 工具调用严格白名单，参数校验，禁止任意查询。
- prompt 注入缓解：system 明确指令优先级，工具参数不直接拼接 SQL（用参数化 + 枚举白名单）。

## 九、依赖与迁移

- 新增 NuGet：无必须项（用 `System.Net.Http.Json` 手写 OpenAI 兼容协议即可）；可选 `Microsoft.Extensions.Http`（含于框架）。
- 新增前端依赖：`@qiun/ucharts`。
- 数据库：**无需改表**（若需存 AI 对话历史，后续加 `ai_chat_message` 表，本期省略）。

## 十、实施计划（建议分期）

| 阶段 | 内容 | 产出 |
|---|---|---|
| S1 | AI 基础设施（AiOptions、ILlmClient、OpenAiCompatClient、DI、启动校验、Mock 降级） | 可调用 LLM 的骨架 + 集成测试 |
| S2 | 大屏后端：DashboardService + 6 个统计端点 + 缓存 | 端点 + 集成测试 |
| S3 | 大屏前端：dashboard 页 + uCharts + 指标卡 | H5 可预览 |
| S4 | AI 工具注册表 + 智能客服（`/api/ai/chat`）+ 前端助手页 | 对话可用 |
| S5 | 内容发布辅助（`/api/ai/assist/publish`）+ 发布页按钮 | 表单填充可用 |
| S6 | 大屏 AI 解读/问答（`/api/ai/insight`、`/api/ai/ask`） | 大屏接入 AI 卡片 |
| S7 | 文档 + 集成测试 + 冒烟 | 收尾 |

每期以 background terminal 构建；集成测试沿用现有 `AgroMarket.Tests`（打已运行后端），新增 section 覆盖 AI（Mock LLM）与大屏端点。

## 十一、未决问题

1. LLM 供应商最终选定？（默认 DeepSeek，OpenAI 兼容即可切换）
2. AI 对话历史是否需要持久化（跨会话/跨设备）？本期默认内存临时。
3. 大屏是否需要投屏大字体/暗色主题？（本期先做移动优先亮色，后续可加主题）
4. 频控阈值与输入长度上限的具体数值。
```

