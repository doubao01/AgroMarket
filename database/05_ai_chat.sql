-- =====================================================================
-- AgroMarket AI 对话 - 持久化 (05)
-- 新增: ai_conversation 会话 / ai_message 消息
-- 可重复执行: CREATE 使用 IF NOT EXISTS
-- 说明: 将 AI 客服对话从进程内内存字典迁移为持久化存储, 支持跨会话/跨设备。
-- =====================================================================

USE agro_market;

-- ---------------------------------------------------------------
-- A1. ai_conversation AI 会话 (每用户多会话)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ai_conversation (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  user_id         BIGINT UNSIGNED NOT NULL COMMENT '所属用户',
  session_id      VARCHAR(64)  NOT NULL COMMENT '对外会话标识(客户端持有)',
  title           VARCHAR(80)  NULL COMMENT '会话标题(取首条用户消息摘要)',
  message_count   INT NOT NULL DEFAULT 0 COMMENT '消息条数(不含系统提示)',
  last_message_at DATETIME(3)  NULL COMMENT '最后一条消息时间(列表排序)',
  created_at      DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at      DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_ai_conv_session (session_id),
  KEY idx_ai_conv_user (user_id, last_message_at),
  CONSTRAINT fk_ai_conv_user FOREIGN KEY (user_id) REFERENCES sys_user(id)
) ENGINE=InnoDB COMMENT='AI 对话会话表';

-- ---------------------------------------------------------------
-- A2. ai_message AI 消息 (仅 USER/ASSISTANT, 不含工具轮次明细)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ai_message (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  conversation_id BIGINT UNSIGNED NOT NULL,
  role            VARCHAR(16)  NOT NULL COMMENT 'USER/ASSISTANT',
  content         TEXT         NOT NULL COMMENT '消息正文',
  tools_used      VARCHAR(400) NULL COMMENT '本轮调用的工具名(JSON 数组)',
  tokens          INT NOT NULL DEFAULT 0 COMMENT '本轮 token 消耗(assistant)',
  created_at      DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_ai_msg_conv (conversation_id, id),
  CONSTRAINT fk_ai_msg_conv FOREIGN KEY (conversation_id) REFERENCES ai_conversation(id)
) ENGINE=InnoDB COMMENT='AI 对话消息表';