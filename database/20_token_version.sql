-- 20. JWT 服务端吊销: sys_user.token_version
-- ---------------------------------------------------------------
-- 之前 JWT 是纯无状态的 7 天票据: 退出登录只清客户端本地副本, 服务端没有任何
-- 吊销手段 —— 泄露的 token 在这 7 天里始终有效, 冻结账号也照样通行。
-- token_version 与 token 里的 tv claim 比对, 不等即失效。
-- 退出登录 / 修改密码时自增, 使该用户此前签发的所有 token 立即作废
-- (即"退出登录会退出所有设备", 这是本方案的取舍, 见 AuthController.Logout 注释)。
--
-- 兼容 MySQL 8 / MariaDB, 幂等可重复执行。
USE agro_market;

SET @sql = (
  SELECT IF(
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_schema = DATABASE() AND table_name = 'sys_user' AND column_name = 'token_version') = 0,
    'ALTER TABLE sys_user ADD COLUMN token_version INT NOT NULL DEFAULT 0 COMMENT ''JWT 版本, 自增即让旧 token 全部失效''',
    'DO 0'));
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
