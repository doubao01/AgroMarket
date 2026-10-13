-- =====================================================================
-- AgroMarket 站内聊天 - 维护迁移 (06)
-- 1) chat_participant 增加 remark (会话备注名, 仅自己可见)
-- 2) 删除历史遗留死表 chat_message_receipt / chat_attachment (全仓库无引用)
-- 可重复执行: 追加列用 information_schema 判定的动态 ALTER (兼容 MySQL 8 与 MariaDB); DROP 使用 IF EXISTS
-- =====================================================================

USE agro_market;

-- chat_participant: 会话备注名 (MySQL 8 与 MariaDB 的 IF NOT EXISTS 语法不通用, 用 information_schema 判定)
SET @c := (SELECT COUNT(*) FROM information_schema.columns
           WHERE table_schema=DATABASE() AND table_name='chat_participant' AND column_name='remark');
SET @s := IF(@c=0,
  'ALTER TABLE chat_participant ADD COLUMN remark VARCHAR(64) NULL COMMENT ''我对该会话/对方的备注名(仅自己可见)''',
  'DO 0');
PREPARE _st FROM @s; EXECUTE _st; DEALLOCATE PREPARE _st;

-- 清理未被使用的历史表 (送达/已读回执与附件均由 chat_message 承载)
DROP TABLE IF EXISTS chat_message_receipt;
DROP TABLE IF EXISTS chat_attachment;
