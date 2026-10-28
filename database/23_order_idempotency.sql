-- =====================================================================
-- AgroMarket 下单幂等迁移 (23)
-- 背景 (07-consistency C3):
--   聊天"直接下单"每次调用都会新建一条 ACCEPTED 报价再走下单流程。
--   订单落库成功但随后的会话卡片推送 (SendBusinessAsync) 抛错时, 接口返回 500,
--   客户端重试同一请求会再建一条报价 + 再扣一次库存 + 再生成一张订单 ——
--   两张订单、两次扣减, 买方只能手动取消其一。
--   直购路径不接收任何幂等键, 无法识别"这是同一次下单的重试"。
-- 方案: trade_order 增加 client_order_id, 与 buyer_id 组成唯一键;
--   同一买家 + 同一幂等键只允许一条订单, 重试命中已有订单直接返回。
--   client_order_id 为 NULL 时不参与唯一性, 兼容所有既有下单路径。
-- 兼容 MySQL 8 / MariaDB, 幂等可重复执行。
-- =====================================================================
USE agro_market;

-- 1. 客户端下单幂等键
SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_schema = DATABASE() AND table_name = 'trade_order' AND column_name = 'client_order_id') = 0,
    'ALTER TABLE trade_order ADD COLUMN client_order_id VARCHAR(64) NULL COMMENT ''客户端下单幂等键'' AFTER order_no',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 2. 唯一键: 同一买家 + 同一幂等键只允许一条订单 (NULL 不参与唯一性)
SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.statistics
      WHERE table_schema = DATABASE() AND table_name = 'trade_order' AND index_name = 'uk_order_client') = 0,
    'ALTER TABLE trade_order ADD UNIQUE KEY uk_order_client (buyer_id, client_order_id)',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
