-- =====================================================================
-- AgroMarket 站内聊天 - 微信式增强 (04)
-- 新增: 消息撤回 / 引用回复 / 送达回执游标
-- 可重复执行: ALTER 使用 IF NOT EXISTS(MariaDB)
-- =====================================================================

USE agro_market;

-- chat_message: 引用回复目标 + 撤回时间
ALTER TABLE chat_message
  ADD COLUMN IF NOT EXISTS reply_to_message_id BIGINT UNSIGNED NULL COMMENT '引用回复的消息ID',
  ADD COLUMN IF NOT EXISTS recalled_at          DATETIME(3)     NULL COMMENT '撤回时间(status=RECALLED 时非空)';

-- chat_participant: 送达游标 (已读游标 last_read_message_id 已存在)
ALTER TABLE chat_participant
  ADD COLUMN IF NOT EXISTS last_delivered_message_id BIGINT UNSIGNED NULL COMMENT '已送达到的消息ID';
