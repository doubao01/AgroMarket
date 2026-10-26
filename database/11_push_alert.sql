-- =====================================================================
-- AgroMarket 消息推送与报警 迁移 (11)
-- alert_rule: 内置报警规则(阈值/开关/抑制窗口, 管理员后台可调)
-- alert_record: 报警命中记录 (rule_code + object_id 唯一, 抑制窗口内去重)
-- push_log: 推送日志 (WECHAT/WS 通道结果)
-- wx_subscribe_quota: 小程序订阅消息剩余额度计数
-- 兼容 MySQL 8 / MariaDB, 幂等可重复执行
-- =====================================================================
USE agro_market;

CREATE TABLE IF NOT EXISTS alert_rule (
  code            VARCHAR(40) NOT NULL,
  enabled         TINYINT(1) NOT NULL DEFAULT 1,
  threshold_json  VARCHAR(500) NOT NULL DEFAULT '{}',
  suppress_hours  INT NOT NULL DEFAULT 24,
  recipient_scope VARCHAR(20) NOT NULL DEFAULT 'ADMIN_ONLY',
  updated_at      DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (code)
) ENGINE=InnoDB COMMENT='报警规则表';

CREATE TABLE IF NOT EXISTS alert_record (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  rule_code   VARCHAR(40) NOT NULL,
  object_type VARCHAR(20) NOT NULL DEFAULT '' COMMENT '业务对象类型: SUPPLY/DEMAND/ORDER/QUOTE, 汇总类规则为空',
  object_id   BIGINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '业务对象ID; TRADE_ANOMALY 固定0',
  status      VARCHAR(20) NOT NULL DEFAULT 'OPEN' COMMENT 'OPEN/RESOLVED/MUTED',
  hit_count   INT NOT NULL DEFAULT 1,
  detail      VARCHAR(500) NULL,
  resolved_at DATETIME(3) NULL,
  notified_at DATETIME(3) NULL COMMENT '首次成功投递时间; NULL 表示尚未投递成功, 下轮扫描会重试',
  created_at  DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  -- 只按 (rule_code, object_id) 去重会让"供应#5"与"采购#5"互相抑制, 必须带类型维度
  UNIQUE KEY uk_alert_v2 (rule_code, object_type, object_id),
  KEY idx_alert_created (created_at),
  CONSTRAINT fk_alert_rule FOREIGN KEY (rule_code) REFERENCES alert_rule(code)
) ENGINE=InnoDB COMMENT='报警记录表';

CREATE TABLE IF NOT EXISTS push_log (
  id           BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  message_id   BIGINT UNSIGNED NULL,
  channel      VARCHAR(20) NOT NULL COMMENT 'WECHAT/WS',
  receiver_id  BIGINT UNSIGNED NOT NULL,
  template_key VARCHAR(20) NULL,
  status       VARCHAR(20) NOT NULL COMMENT 'SENT/FAILED/UNSUBSCRIBED/SKIPPED',
  error        VARCHAR(500) NULL,
  created_at   DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_pl_receiver (receiver_id, created_at)
) ENGINE=InnoDB COMMENT='推送日志表';

CREATE TABLE IF NOT EXISTS wx_subscribe_quota (
  user_id      BIGINT UNSIGNED NOT NULL,
  template_key VARCHAR(20) NOT NULL,
  quota        INT NOT NULL DEFAULT 0,
  updated_at   DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (user_id, template_key),
  CONSTRAINT fk_wsq_user FOREIGN KEY (user_id) REFERENCES sys_user(id)
) ENGINE=InnoDB COMMENT='微信订阅消息额度表';

-- 内置 6 条报警规则 (INSERT IGNORE 防覆盖管理员修改)
INSERT IGNORE INTO alert_rule (code, enabled, threshold_json, suppress_hours, recipient_scope) VALUES
  ('ORDER_PAY_TIMEOUT', 1, '{"hours":24}',   24, 'PARTY_AND_ADMIN'),
  ('QUOTE_NO_RESPONSE', 1, '{"hours":48}',   24, 'PARTY_AND_ADMIN'),
  ('LISTING_EXPIRED',   1, '{}',             24, 'PUBLISHER'),
  ('AUDIT_BACKLOG',     1, '{"count":20}',   24, 'ADMIN_ONLY'),
  ('TRADE_ANOMALY',     1, '{"percent":50}', 24, 'ADMIN_ONLY'),
  ('FED_SYNC_STALE',    1, '{}',             24, 'ADMIN_ONLY');
