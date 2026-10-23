-- =====================================================================
-- AgroMarket 支付幂等与退款补偿迁移 (14)
-- 背景:
--  1) payment_record 没有幂等键: 每次点支付都插一条 INIT, 同一订单可刷出多张可支付单;
--     回调按 (order_id, channel) 批量更新, 会把该订单所有历史记录一起刷成 SUCCESS
--     并覆盖 trade_no, 真实交易号被最后一条覆盖。
--  2) 支付回调晚于订单取消时, 钱已收但单已取消, 系统没有任何退款标记。
-- 兼容 MySQL 8 / MariaDB, 幂等可重复执行。
-- =====================================================================
USE agro_market;

-- 1. 客户端幂等键 (同一订单+渠道+token 只允许一条)
SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_schema = DATABASE() AND table_name = 'payment_record' AND column_name = 'client_token') = 0,
    'ALTER TABLE payment_record ADD COLUMN client_token VARCHAR(64) NULL COMMENT ''客户端幂等键'' AFTER channel',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 2. 退款标记: 回调发现订单已取消时置 PENDING, 由人工或后续任务处理
SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_schema = DATABASE() AND table_name = 'payment_record' AND column_name = 'refund_status') = 0,
    'ALTER TABLE payment_record ADD COLUMN refund_status VARCHAR(20) NOT NULL DEFAULT '''' COMMENT ''空/ PENDING / DONE'' AFTER status',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 3. 幂等唯一键 (client_token 为 NULL 时不参与唯一性, 兼容历史数据)
SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.statistics
      WHERE table_schema = DATABASE() AND table_name = 'payment_record' AND index_name = 'uk_pr_client') = 0,
    'ALTER TABLE payment_record ADD UNIQUE KEY uk_pr_client (order_id, channel, client_token)',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 4. 回调按 (order_id, channel, status) 精确定位待确认的那一条
SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.statistics
      WHERE table_schema = DATABASE() AND table_name = 'payment_record' AND index_name = 'idx_pr_order_channel_status') = 0,
    'ALTER TABLE payment_record ADD KEY idx_pr_order_channel_status (order_id, channel, status)',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 5. 订单超时关闭任务需要按状态+时间筛选
SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.statistics
      WHERE table_schema = DATABASE() AND table_name = 'trade_order' AND index_name = 'idx_order_status_created') = 0,
    'ALTER TABLE trade_order ADD KEY idx_order_status_created (status, created_at)',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 6. 待退款记录的人工处理索引
SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.statistics
      WHERE table_schema = DATABASE() AND table_name = 'payment_record' AND index_name = 'idx_pr_refund') = 0,
    'ALTER TABLE payment_record ADD KEY idx_pr_refund (refund_status, created_at)',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
