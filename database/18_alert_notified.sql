-- 18. 报警记录通知状态
-- ---------------------------------------------------------------
-- 扫描是"先落 alert_record, 再发通知"。若通知没发出去(站内消息写入失败、
-- 接收人解析报错), 记录却已经存在, 下一轮扫描会按抑制窗口判定"已通知过"而跳过,
-- 于是这条告警在窗口期内(默认数小时)永远不会再通知任何人 —— 运维视角是彻底静默。
-- 这里记录"是否成功投递", NULL 表示从未投递成功, 下一轮扫描会重试而不是抑制。
--
-- 兼容 MySQL 8 / MariaDB, 幂等可重复执行。
USE agro_market;

SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_schema = DATABASE() AND table_name = 'alert_record' AND column_name = 'notified_at') = 0,
    'ALTER TABLE alert_record ADD COLUMN notified_at DATETIME(3) NULL COMMENT ''首次成功投递时间; NULL 表示尚未投递成功, 下轮扫描会重试''',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
