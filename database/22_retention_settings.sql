-- 22. 留存期限后台可设置
-- ---------------------------------------------------------------
-- V12 的 RetentionWorker 原本从 appsettings 读期限, 改起来要改配置+重启。
-- 留存期限是运营/合规会反复调整的数字, 应当后台可改。
--
-- retention_setting 与 fed_setting 同构: 只存"覆盖值", 未写入的表项
-- 回落到代码默认值 (RetentionSettings.Defaults)。迁移写入的是当前默认值,
-- 管理员在后台改数字仅更新本表, 无需重启。
USE agro_market;

CREATE TABLE IF NOT EXISTS retention_setting (
  setting_key   VARCHAR(64)  NOT NULL COMMENT '设置项: Enabled/ChatMessageDays/NotificationDays/OperationLogDays/FederationDays/SmsCodeDays',
  setting_value VARCHAR(512) NULL     COMMENT '设置值(字符串)',
  updated_at    DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (setting_key)
) ENGINE=InnoDB COMMENT='数据留存设置覆盖(默认值在代码 RetentionSettings.Defaults)';

INSERT INTO retention_setting (setting_key, setting_value) VALUES
  ('Enabled',           'false'),
  ('ChatMessageDays',   '365'),
  ('NotificationDays',  '180'),
  ('OperationLogDays',  '180'),
  ('FederationDays',    '365'),
  ('SmsCodeDays',       '7')
ON DUPLICATE KEY UPDATE setting_value = VALUES(setting_value);