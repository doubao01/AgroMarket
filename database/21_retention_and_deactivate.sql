-- 21. 账号注销与数据留存
-- ---------------------------------------------------------------
-- V12: 之前没有任何注销/删除能力, 也没有留存期限 —— 聊天正文、通知、审计日志
-- 只增不减, "存储期限最小化"无法满足。
--
-- 注销做成"匿名化 + 标记", 不做物理删除: 订单/报价是交易凭证, 直接删会让对方
-- 的账目断裂。个人信息(手机号/昵称/头像/联系方式)在注销时抹掉, 因此保留下来的
-- 交易记录不再指向可识别的自然人。
--
-- 手机号会被改写为 del_<id>: 既腾出原号码给本人重新注册(uk_user_phone 仍唯一),
-- 又不会让已注销用户的旧号码留在库里。
--
-- 兼容 MySQL 8 / MariaDB, 幂等可重复执行。
USE agro_market;

SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_schema = DATABASE() AND table_name = 'sys_user' AND column_name = 'deleted_at') = 0,
    'ALTER TABLE sys_user ADD COLUMN deleted_at DATETIME(3) NULL COMMENT ''注销时间, 非空表示已注销(已匿名化)''',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_schema = DATABASE() AND table_name = 'sys_user' AND column_name = 'deleted_reason') = 0,
    'ALTER TABLE sys_user ADD COLUMN deleted_reason VARCHAR(64) NULL COMMENT ''注销原因: USER_SELF / ADMIN''',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 留存清理按时间扫这几张表, 原先只有主键索引, 限期清理会全表扫描。
-- MySQL 8 不支持 CREATE INDEX IF NOT EXISTS(那是 MariaDB 扩展), 故用 information_schema 判断。
SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.statistics
      WHERE table_schema = DATABASE() AND table_name = 'chat_message' AND index_name = 'idx_chat_message_created') = 0,
    'CREATE INDEX idx_chat_message_created ON chat_message (created_at)',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.statistics
      WHERE table_schema = DATABASE() AND table_name = 'user_message' AND index_name = 'idx_user_message_created') = 0,
    'CREATE INDEX idx_user_message_created ON user_message (created_at)',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.statistics
      WHERE table_schema = DATABASE() AND table_name = 'sys_operation_log' AND index_name = 'idx_sys_operation_log_created') = 0,
    'CREATE INDEX idx_sys_operation_log_created ON sys_operation_log (created_at)',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.statistics
      WHERE table_schema = DATABASE() AND table_name = 'fed_report' AND index_name = 'idx_fed_report_received') = 0,
    'CREATE INDEX idx_fed_report_received ON fed_report (received_at)',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
