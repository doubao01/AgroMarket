-- =====================================================================
-- AgroMarket 支付凭据落库迁移 (15)
-- 背景:
--   第 5 轮引入"命中既有 INIT 单就复用、不再向渠道重复下单"后, 渠道首次返回的
--   二维码/收银台地址没有被持久化。用户关掉页面再点支付 → 命中复用分支 → payUrl
--   为 null, 前端无码可扫, 这笔钱永远付不出去。
--   本迁移为 payment_record 增加 pay_url, 由 PaymentService 在 INIT 时写入、
--   复用分支原样回传。
-- 兼容 MySQL 8 / MariaDB, 幂等可重复执行。
-- =====================================================================
USE agro_market;

SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_schema = DATABASE() AND table_name = 'payment_record' AND column_name = 'pay_url') = 0,
    'ALTER TABLE payment_record ADD COLUMN pay_url VARCHAR(1024) NULL COMMENT ''渠道返回的支付地址/二维码内容'' AFTER trade_no',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
