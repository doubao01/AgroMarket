# Requirements Document: 消息推送与报警

Feature Name: message-push-alert
Updated: 2026-10-13

## Introduction

为 AgroMarket 增加统一的消息推送与业务报警能力。在现有站内消息中心（`user_message`）与聊天 WebSocket 通道基础上，扩展：

1. **站内实时推送**：业务事件（报价/订单/审核/系统）产生后即时推送至在线用户，客户端角标与弹窗实时更新。
2. **微信订阅消息**：小程序端在关键节点（新报价、订单状态变更、审核结果）通过微信订阅消息模板触达离线用户。
3. **业务与运维报警**：内置默认报警规则（订单超时未支付、报价长期未响应、供需过期、审核积压、联动同步 stale、成交额异常波动），报警生成站内消息并可推送给管理员；阈值与开关由管理员后台调整。

## Glossary

- **站内消息（user_message）**：现有站内信表，本功能复用为推送落库终点。
- **实时通道（WS）**：现有聊天 WebSocket `/ws/chat`，扩展为可承载通知类消息帧。
- **订阅消息**：微信小程序一次性订阅消息（`subscribeMessage.send`），需用户预先授权订阅。
- **报警规则（Alert Rule）**：由定时扫描任务执行的可配置检测条件，含阈值与开关。
- **报警事件（Alert）**：规则命中后产生的记录，去重后生成站内消息并推送。
- **报警接收人（Recipient）**：默认为全部 ACTIVE 管理员；业务类报警接收人为业务当事用户。
- **报警抑制（Suppression）**：同一报警对象在同一抑制窗口内只报一次，防止重复轰炸。

## Requirements

### R1 站内实时推送

**User Story:** 作为用户，我希望业务事件产生的消息能实时到达我的客户端，以便第一时间处理报价与订单。

#### Acceptance Criteria

1. WHEN 业务服务调用消息发送接口产生新站内消息，THE 系统 SHALL 通过 WebSocket 通道向该接收人的全部在线会话推送一条通知帧（含 messageId/msgType/title/bizId）。
2. WHEN 接收人当前无在线 WebSocket 会话，THE 系统 SHALL 仅落库 `user_message`，客户端下次打开时通过未读数接口补齐。
3. WHILE 客户端已建立 WebSocket 连接，THE 客户端 SHALL 在收到通知帧后 1 秒内更新全局未读角标与消息页列表。
4. IF 通知帧推送失败（连接中断/序列化异常），THE 系统 SHALL 保留已落库消息并忽略本次推送错误，主业务流程 SHALL 继续。

### R2 微信订阅消息

**User Story:** 作为小程序用户，我希望关键业务节点（新报价/订单状态/审核结果）能通过微信服务通知触达我，以便离线时不遗漏。

#### Acceptance Criteria

1. WHEN 用户在小程序内触发需要后续通知的业务动作（提交报价、创建订单、发布供应/采购），THE 小程序 SHALL 请求对应模板的一次性订阅授权并上报授权结果。
2. WHEN 站内消息生成且消息类型命中已配置的订阅模板、接收人存在有效 openid 且该用户对该模板有剩余订阅次数，THE 系统 SHALL 调用微信订阅消息接口发送，并将发送结果记录到推送日志。
3. IF 微信接口返回错误（无订阅额度/模板失效/令牌过期），THE 系统 SHALL 记录失败原因并按消息类型降级为仅站内消息，业务流程 SHALL 继续。
4. WHILE 微信 access_token 有效期内，THE 系统 SHALL 复用缓存的令牌，且在令牌到期前刷新。

### R3 业务报警（内置默认规则）

**User Story:** 作为管理员，我希望系统自动发现业务异常（超时未支付、报价无响应、供需过期、审核积压、成交异常波动）并提醒我，以便及时干预。

#### Acceptance Criteria

1. WHEN 存在订单处于 TO_PAY 状态且超过 `order_pay_timeout_hours`（默认 24）小时，THE 系统 SHALL 生成"订单支付超时"报警并推送订单的买卖双方与全部管理员。
2. WHEN 存在报价处于 PENDING 状态且超过 `quote_response_timeout_hours`（默认 48）小时，THE 系统 SHALL 生成"报价响应超时"报警并推送报价双方与全部管理员。
3. WHEN 存在供应/采购处于 ACTIVE 且 `expire_at` 已过，THE 系统 SHALL 生成"信息已过期"报警并推送发布者。
4. WHEN 处于 PENDING 状态的供应/采购数量之和超过 `audit_backlog_threshold`（默认 20）条，THE 系统 SHALL 生成"审核积压"报警并仅推送全部管理员。
5. WHEN 当日真实成交额相对前 7 日日均偏离超过 `trade_drop_percent`（默认 ±50%）且前 7 日日均成交额大于 0，THE 系统 SHALL 在每日固定扫描时生成"成交额异常波动"报警并推送全部管理员。
6. WHILE 同一报警对象处于抑制窗口（`alert_suppress_hours`，默认 24）内，THE 系统 SHALL 生成报警记录但 SHALL NOT 重复发送站内消息与推送。

### R4 运维报警

**User Story:** 作为管理员，我希望节点联动同步异常被自动上报，以便在数据断流时及时排查。

#### Acceptance Criteria

1. WHEN 某已登记对端节点的最后成功同步时间超过 `Federation:HealthStaleMinutes`（复用现有健康阈值），THE 每日/每小时扫描 SHALL 生成"联动同步 stale"报警并推送全部管理员。
2. WHEN 某对端节点此前已 stale 且在抑制窗口内恢复健康，THE 系统 SHALL 生成"联动同步恢复"通知并推送全部管理员。

### R5 报警规则配置

**User Story:** 作为管理员，我希望在后台查看和调整各报警规则的阈值与开关，以便按运营节奏控制报警量。

#### Acceptance Criteria

1. THE 系统 SHALL 提供 `alert_rule` 存储，内置 R3/R4 全部规则行（code、enabled、阈值 JSON、抑制窗口、接收人范围）。
2. WHEN 管理员通过后台接口修改某规则的 enabled 或阈值，THE 系统 SHALL 校验取值范围并在下一次扫描周期生效，SHALL NOT 要求服务重启。
3. IF 管理员提交非法阈值（负数/超上限），THE 系统 SHALL 返回 400 并保留原值。
4. THE 系统 SHALL 提供管理员接口返回近期报警记录列表（时间/规则/对象/状态），供后台展示。

### R6 扫描与执行

**User Story:** 作为系统，我需要可靠且低干扰的报警扫描调度。

#### Acceptance Criteria

1. THE 系统 SHALL 以后台常驻任务按 `alert_scan_interval_minutes`（默认 30，最低 5）执行业务报警扫描；成交额波动与联动 stale 检查每日执行一次（首个扫描周期命中即执行，随后 24 小时内跳过）。
2. WHILE 多实例部署，THE 系统 SHALL 使用数据库咨询锁保证同一时刻仅一个实例执行扫描（复用 `GET_LOCK` 模式）。
3. IF 单条规则执行抛出异常，THE 系统 SHALL 记录错误日志并继续执行其余规则。

### R7 客户端展示

**User Story:** 作为用户/管理员，我希望区分普通通知与报警的视觉呈现。

#### Acceptance Criteria

1. THE 消息中心 SHALL 为 `msg_type='ALERT'` 的消息显示独立警示样式（图标/边框），并与 QUOTE/ORDER/SYSTEM 消息共用列表。
2. THE 管理后台 SHALL 新增"报警中心"入口，展示报警记录列表与各规则当前状态。
3. WHEN 报警推送到 WebSocket，THE 客户端 SHALL 按消息类型复用现有弹窗策略：ALERT 仅更新角标与消息页，SHALL NOT 强弹打断。

## Non-Functional Requirements

1. 报警扫描单轮全量 SQL 执行时间 SHALL 小于 5 秒（各规则查询均走既有索引或新增覆盖索引）。
2. 报警产生到站内消息可见的延迟 SHALL 小于 30 秒。
3. 微信订阅消息发送 SHALL 为异步尽力送达，失败 SHALL NOT 影响站内消息。
