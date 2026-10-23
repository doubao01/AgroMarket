-- =====================================================================
-- AgroMarket 报警对象类型迁移 (13)
-- 背景: alert_record 的唯一键原本是 (rule_code, object_id), 但 object_id 只是
-- 各业务表自己的主键 —— 供应#5 与采购#5 是两个不同对象, 会互相抑制;
-- 按发布者派发通知时用 UNION 拼两张表, 同 id 下会取到另一张表的发布者。
-- 本迁移增加 object_type 维度并重建唯一键。
--
-- 注意: 外键 fk_alert_rule 就建在旧唯一键上, 直接 DROP INDEX 会报
-- 1553 "needed in a foreign key constraint", 因此需要先摘外键再挂回。
-- 兼容 MySQL 8 / MariaDB, 幂等可重复执行。
-- =====================================================================
USE agro_market;

-- 1. 增加 object_type 列
SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_schema = DATABASE() AND table_name = 'alert_record' AND column_name = 'object_type') = 0,
    'ALTER TABLE alert_record ADD COLUMN object_type VARCHAR(20) NOT NULL DEFAULT '''' COMMENT ''业务对象类型: SUPPLY/DEMAND/ORDER/QUOTE, 汇总类规则为空'' AFTER rule_code',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 2. 回填历史数据 (LISTING_EXPIRED 需要按实际存在的表判定)
UPDATE alert_record SET object_type='ORDER'
  WHERE rule_code='ORDER_PAY_TIMEOUT' AND object_type='';
UPDATE alert_record SET object_type='QUOTE'
  WHERE rule_code='QUOTE_NO_RESPONSE' AND object_type='';
UPDATE alert_record SET object_type='SUPPLY'
  WHERE rule_code='LISTING_EXPIRED' AND object_type=''
    AND EXISTS (SELECT 1 FROM product_supply s WHERE s.id = alert_record.object_id);
UPDATE alert_record SET object_type='DEMAND'
  WHERE rule_code='LISTING_EXPIRED' AND object_type=''
    AND EXISTS (SELECT 1 FROM purchase_demand d WHERE d.id = alert_record.object_id);

-- 3. 摘下外键(它绑在旧唯一键上, 不摘掉旧键就删不掉)
SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.table_constraints
      WHERE table_schema = DATABASE() AND table_name = 'alert_record' AND constraint_name = 'fk_alert_rule') > 0,
    'ALTER TABLE alert_record DROP FOREIGN KEY fk_alert_rule',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 4. 删除旧唯一键 (rule_code, object_id)
SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.statistics
      WHERE table_schema = DATABASE() AND table_name = 'alert_record' AND index_name = 'uk_alert') > 0,
    'ALTER TABLE alert_record DROP INDEX uk_alert',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 5. 建带类型维度的新唯一键
SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.statistics
      WHERE table_schema = DATABASE() AND table_name = 'alert_record' AND index_name = 'uk_alert_v2') = 0,
    'ALTER TABLE alert_record ADD UNIQUE KEY uk_alert_v2 (rule_code, object_type, object_id)',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 6. 挂回外键
SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.table_constraints
      WHERE table_schema = DATABASE() AND table_name = 'alert_record' AND constraint_name = 'fk_alert_rule') = 0,
    'ALTER TABLE alert_record ADD CONSTRAINT fk_alert_rule FOREIGN KEY (rule_code) REFERENCES alert_rule(code)',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
