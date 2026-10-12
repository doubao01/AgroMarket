-- =====================================================================
-- AgroMarket 节点联动 (数据上报 / 同步) 迁移 (07)
-- 扁平多节点: 省市县节点互为对等, 上报汇总指标 + 脱敏业务明细, 支持双向推送/拉取
-- 鉴权: JWT 节点令牌 (见 FederationTokenService)
-- 可重复执行: CREATE 使用 IF NOT EXISTS
-- =====================================================================

USE agro_market;

-- ---------------------------------------------------------------
-- F1. fed_node 已知对端节点注册表 (本节点身份来自配置, 不在此表)
--     node_token: 调用该对端时使用的 JWT 节点令牌
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS fed_node (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  node_code     VARCHAR(64)  NOT NULL COMMENT '节点编码(全局唯一)',
  node_name     VARCHAR(128) NOT NULL COMMENT '节点名称',
  node_level    VARCHAR(16)  NOT NULL COMMENT 'PROVINCE/CITY/COUNTY',
  region_code   VARCHAR(20)  NULL COMMENT '行政区划编码',
  base_url      VARCHAR(300) NOT NULL COMMENT '对端接口基址(不含 /api)',
  node_token    VARCHAR(1024) NULL COMMENT '调用该对端的 JWT 节点令牌',
  direction     VARCHAR(8)   NOT NULL DEFAULT 'BOTH' COMMENT 'PUSH/PULL/BOTH',
  status        VARCHAR(16)  NOT NULL DEFAULT 'ACTIVE' COMMENT 'ACTIVE/DISABLED',
  last_push_at  DATETIME(3)  NULL,
  last_pull_at  DATETIME(3)  NULL,
  last_error    VARCHAR(300) NULL,
  created_at    DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at    DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_fed_node_code (node_code)
) ENGINE=InnoDB COMMENT='联动对端节点注册表';

-- ---------------------------------------------------------------
-- F2. fed_report 上报批次 (接收方审计, 幂等键 source+批次号)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS fed_report (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  source_node_code VARCHAR(64)  NOT NULL COMMENT '来源节点',
  batch_no         VARCHAR(64)  NOT NULL COMMENT '批次号(幂等)',
  period_date      DATE         NOT NULL COMMENT '数据日期',
  metric_count     INT          NOT NULL DEFAULT 0,
  record_count     INT          NOT NULL DEFAULT 0,
  payload          JSON         NULL COMMENT '原始上报载荷(审计)',
  received_at      DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_fed_report (source_node_code, batch_no),
  KEY idx_fed_report_time (received_at)
) ENGINE=InnoDB COMMENT='联动上报批次';

-- ---------------------------------------------------------------
-- F3. fed_daily_metric 按天汇总指标读模型 (接收方存储, 幂等 upsert)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS fed_daily_metric (
  id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  source_node_code  VARCHAR(64)  NOT NULL COMMENT '来源节点',
  stat_date         DATE         NOT NULL COMMENT '统计日期',
  user_count        BIGINT       NOT NULL DEFAULT 0,
  active_user_count BIGINT       NOT NULL DEFAULT 0,
  new_user_count    BIGINT       NOT NULL DEFAULT 0,
  supply_count      BIGINT       NOT NULL DEFAULT 0,
  demand_count      BIGINT       NOT NULL DEFAULT 0,
  order_count       BIGINT       NOT NULL DEFAULT 0,
  trade_amount      DECIMAL(18,2) NOT NULL DEFAULT 0,
  message_count     BIGINT       NOT NULL DEFAULT 0,
  conversation_count BIGINT      NOT NULL DEFAULT 0,
  updated_at        DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_fed_metric (source_node_code, stat_date)
) ENGINE=InnoDB COMMENT='联动按天汇总指标';

-- ---------------------------------------------------------------
-- F4. fed_business_record 脱敏业务明细 (接收方存储, 幂等 upsert)
--     data 中不含任何用户身份/联系方式, 仅结构化业务字段(已二次脱敏)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS fed_business_record (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  source_node_code VARCHAR(64)  NOT NULL COMMENT '来源节点',
  record_type      VARCHAR(16)  NOT NULL COMMENT 'ORDER/SUPPLY/DEMAND',
  external_id      VARCHAR(80)  NOT NULL COMMENT '来源侧业务ID(含节点前缀)',
  region_code      VARCHAR(20)  NULL COMMENT '行政区划编码',
  occurred_at      DATETIME(3)  NOT NULL COMMENT '业务发生时间',
  data             JSON         NOT NULL COMMENT '脱敏明细',
  received_at      DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_fed_record (source_node_code, record_type, external_id),
  KEY idx_fed_record_time (occurred_at),
  KEY idx_fed_record_region (region_code)
) ENGINE=InnoDB COMMENT='联动脱敏业务明细';

-- ---------------------------------------------------------------
-- F5. fed_sync_cursor 主动拉取游标 (每对端每类型一条)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS fed_sync_cursor (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  peer_node_code VARCHAR(64)  NOT NULL COMMENT '对端节点',
  cursor_type    VARCHAR(16)  NOT NULL COMMENT 'RECORD/METRIC',
  last_cursor    VARCHAR(64)  NULL COMMENT '上次拉取的游标(时间戳/日期)',
  updated_at     DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_fed_cursor (peer_node_code, cursor_type)
) ENGINE=InnoDB COMMENT='联动拉取游标';
