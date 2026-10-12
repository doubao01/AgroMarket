-- =====================================================================
-- AgroMarket 站内聊天模块 - 结构迁移 (03)
-- MySQL 8 / MariaDB 10.11 兼容, utf8mb4
-- 可重复执行: CREATE 使用 IF NOT EXISTS; ALTER 使用 IF NOT EXISTS(MariaDB)
-- 说明: 面向已有库的增量迁移。全新安装同样会执行本脚本。
-- =====================================================================

USE agro_market;

-- ---------------------------------------------------------------
-- C1. chat_conversation 会话
--     conv_key 为双方唯一去重键: min(uid):max(uid):sourceType:sourceId
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS chat_conversation (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  conv_key        VARCHAR(96)  NOT NULL COMMENT '会话去重键 min(uid):max(uid):sourceType:sourceId',
  type            VARCHAR(16)  NOT NULL DEFAULT 'P2P' COMMENT 'P2P(当前仅支持一对一)',
  source_type     VARCHAR(20)  NULL COMMENT '来源: PRODUCT/SUPPLY/DEMAND/SUPPLIER',
  source_id       BIGINT UNSIGNED NULL COMMENT '来源业务对象ID',
  last_message_id BIGINT UNSIGNED NULL COMMENT '最后一条消息ID',
  last_message_at DATETIME(3)  NULL COMMENT '最后消息时间(会话列表排序)',
  created_at      DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at      DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_conv_key (conv_key),
  KEY idx_conv_last (last_message_at)
) ENGINE=InnoDB COMMENT='聊天会话表';

-- ---------------------------------------------------------------
-- C2. chat_participant 会话成员 (未读/置顶/免打扰/隐藏 各自独立)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS chat_participant (
  id                   BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  conversation_id      BIGINT UNSIGNED NOT NULL,
  user_id              BIGINT UNSIGNED NOT NULL,
  last_read_message_id BIGINT UNSIGNED NULL COMMENT '已读到的消息ID',
  unread_count         INT NOT NULL DEFAULT 0 COMMENT '该成员未读数',
  is_pinned            TINYINT(1) NOT NULL DEFAULT 0 COMMENT '置顶',
  is_muted             TINYINT(1) NOT NULL DEFAULT 0 COMMENT '免打扰',
  is_hidden            TINYINT(1) NOT NULL DEFAULT 0 COMMENT '隐藏/删除(软删)',
  joined_at            DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_cp_conv_user (conversation_id, user_id),
  KEY idx_cp_user_list (user_id, is_hidden, is_pinned),
  CONSTRAINT fk_cp_conv FOREIGN KEY (conversation_id) REFERENCES chat_conversation(id),
  CONSTRAINT fk_cp_user FOREIGN KEY (user_id) REFERENCES sys_user(id)
) ENGINE=InnoDB COMMENT='会话成员表';

-- ---------------------------------------------------------------
-- C3. chat_message 消息
--     (conversation_id, client_msg_id) 唯一 => 幂等, 防断线重发重复
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS chat_message (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  conversation_id BIGINT UNSIGNED NOT NULL,
  sender_id       BIGINT UNSIGNED NOT NULL,
  client_msg_id   VARCHAR(64)  NULL COMMENT '客户端消息唯一标识(幂等)',
  message_type    VARCHAR(20)  NOT NULL DEFAULT 'TEXT'
                  COMMENT 'TEXT/IMAGE/FILE/PRODUCT_CARD/INQUIRY/QUOTE/ORDER_CARD/SYSTEM',
  content         VARCHAR(2000) NULL COMMENT '文本内容或摘要',
  payload         JSON         NULL COMMENT '结构化消息载荷(商品/报价/订单快照)',
  biz_type        VARCHAR(20)  NULL COMMENT '关联业务: SUPPLY/DEMAND/QUOTE/ORDER',
  biz_id          BIGINT UNSIGNED NULL COMMENT '关联业务ID',
  status          VARCHAR(16)  NOT NULL DEFAULT 'SENT' COMMENT 'SENT/RECALLED',
  created_at      DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_cm_client (conversation_id, client_msg_id),
  KEY idx_cm_conv (conversation_id, id),
  KEY idx_cm_biz (biz_type, biz_id),
  CONSTRAINT fk_cm_conv FOREIGN KEY (conversation_id) REFERENCES chat_conversation(id),
  CONSTRAINT fk_cm_sender FOREIGN KEY (sender_id) REFERENCES sys_user(id)
) ENGINE=InnoDB COMMENT='聊天消息表';

-- ---------------------------------------------------------------
-- C4. chat_message_receipt 送达/已读回执
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS chat_message_receipt (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  message_id  BIGINT UNSIGNED NOT NULL,
  user_id     BIGINT UNSIGNED NOT NULL,
  delivered_at DATETIME(3) NULL,
  read_at     DATETIME(3) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uk_cmr_msg_user (message_id, user_id),
  KEY idx_cmr_user (user_id, read_at),
  CONSTRAINT fk_cmr_msg FOREIGN KEY (message_id) REFERENCES chat_message(id),
  CONSTRAINT fk_cmr_user FOREIGN KEY (user_id) REFERENCES sys_user(id)
) ENGINE=InnoDB COMMENT='消息回执表';

-- ---------------------------------------------------------------
-- C5. chat_attachment 消息附件 (图片/文件)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS chat_attachment (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  message_id  BIGINT UNSIGNED NOT NULL,
  file_url    VARCHAR(400) NOT NULL,
  file_type   VARCHAR(20)  NOT NULL COMMENT 'IMAGE/FILE',
  file_size   BIGINT UNSIGNED NULL,
  created_at  DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_ca_msg (message_id),
  CONSTRAINT fk_ca_msg FOREIGN KEY (message_id) REFERENCES chat_message(id)
) ENGINE=InnoDB COMMENT='消息附件表';

-- ---------------------------------------------------------------
-- C6. chat_block 拉黑关系
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS chat_block (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  blocker_id      BIGINT UNSIGNED NOT NULL,
  blocked_user_id BIGINT UNSIGNED NOT NULL,
  created_at      DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_cb_pair (blocker_id, blocked_user_id),
  KEY idx_cb_blocked (blocked_user_id),
  CONSTRAINT fk_cb_blocker FOREIGN KEY (blocker_id) REFERENCES sys_user(id),
  CONSTRAINT fk_cb_blocked FOREIGN KEY (blocked_user_id) REFERENCES sys_user(id)
) ENGINE=InnoDB COMMENT='拉黑关系表';

-- ---------------------------------------------------------------
-- 扩展 trade_quote: 挂接会话与结构化交付条件 (ADR-2)
-- MariaDB 支持 ADD COLUMN IF NOT EXISTS; 全新库 01_schema 已含这些列时此处幂等跳过
-- ---------------------------------------------------------------
ALTER TABLE trade_quote
  ADD COLUMN IF NOT EXISTS conversation_id   BIGINT UNSIGNED NULL COMMENT '来源会话',
  ADD COLUMN IF NOT EXISTS message_id        BIGINT UNSIGNED NULL COMMENT '来源聊天消息',
  ADD COLUMN IF NOT EXISTS delivery_address  VARCHAR(300)    NULL COMMENT '交货地点',
  ADD COLUMN IF NOT EXISTS deliver_by        DATE            NULL COMMENT '期望交货日期',
  ADD COLUMN IF NOT EXISTS freight_bearer    VARCHAR(20)     NULL COMMENT '运费承担: BUYER/SELLER',
  ADD COLUMN IF NOT EXISTS valid_until       DATETIME(3)     NULL COMMENT '报价有效期';

ALTER TABLE trade_quote
  ADD KEY IF NOT EXISTS idx_tq_conv (conversation_id);
