-- =====================================================================
-- AgroMarket 节点联动 运行时设置迁移 (09)
-- fed_setting: 允许管理员在界面动态覆盖部分联动参数 (开关/节奏/汇聚口径),
--   覆盖优先级: 本表 > appsettings/环境变量; 仅"可安全热改"的参数在此表管理。
--   身份与密钥类参数 (Enabled/NodeCode/Level/TokenMode/SharedKey/Role) 仍由部署配置决定, 需重启生效。
-- 可重复执行: CREATE IF NOT EXISTS
-- =====================================================================

USE agro_market;

CREATE TABLE IF NOT EXISTS fed_setting (
  setting_key   VARCHAR(64)  NOT NULL COMMENT '设置项',
  setting_value VARCHAR(512) NULL     COMMENT '设置值(字符串)',
  updated_at    DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (setting_key)
) ENGINE=InnoDB COMMENT='联动运行时设置覆盖';
