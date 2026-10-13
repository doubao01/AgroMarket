-- =====================================================================
-- AgroMarket 站内聊天 - 微信式增强 (04)
-- 新增: 消息撤回 / 引用回复 / 送达回执游标
-- 可重复执行: 追加列用 information_schema 判定的动态 ALTER (兼容 MySQL 8 与 MariaDB)
-- =====================================================================

USE agro_market;

-- chat_message: 引用回复目标 + 撤回时间
SET @c := (SELECT COUNT(*) FROM information_schema.columns
           WHERE table_schema=DATABASE() AND table_name='chat_message' AND column_name='reply_to_message_id');
SET @s := IF(@c=0,
  'ALTER TABLE chat_message ADD COLUMN reply_to_message_id BIGINT UNSIGNED NULL COMMENT ''引用回复的消息ID'', ADD COLUMN recalled_at DATETIME(3) NULL COMMENT ''撤回时间(status=RECALLED 时非空)''',
  'DO 0');
PREPARE _st FROM @s; EXECUTE _st; DEALLOCATE PREPARE _st;

-- chat_participant: 送达游标 (已读游标 last_read_message_id 已存在)
SET @c := (SELECT COUNT(*) FROM information_schema.columns
           WHERE table_schema=DATABASE() AND table_name='chat_participant' AND column_name='last_delivered_message_id');
SET @s := IF(@c=0,
  'ALTER TABLE chat_participant ADD COLUMN last_delivered_message_id BIGINT UNSIGNED NULL COMMENT ''已送达到的消息ID''',
  'DO 0');
PREPARE _st FROM @s; EXECUTE _st; DEALLOCATE PREPARE _st;
