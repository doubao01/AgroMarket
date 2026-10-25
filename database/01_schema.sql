-- =====================================================================
-- AgroMarket 息县农产品自由交易平台 - 数据库结构
-- MySQL 8 / MariaDB 10.11 兼容, utf8mb4
-- 可重复执行: 所有 CREATE 使用 IF NOT EXISTS, 数据种子在 seed 脚本中
-- =====================================================================

CREATE DATABASE IF NOT EXISTS agro_market
  DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE agro_market;

-- ---------------------------------------------------------------
-- 1. sys_region 行政区域表 (省/市/县/乡镇/村 五级)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sys_region (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '系统内部主键',
  region_code   VARCHAR(20)  NOT NULL COMMENT '行政区划编码(国家统计局12位)或系统稳定编码',
  parent_id     BIGINT UNSIGNED NULL COMMENT '上级区域ID, 根节点为NULL',
  region_name   VARCHAR(64)  NOT NULL COMMENT '区域名称',
  region_level  TINYINT      NOT NULL COMMENT '1省 2市 3县区 4乡镇街道 5行政村/社区',
  sort_order    INT          NOT NULL DEFAULT 0,
  is_enabled    TINYINT(1)   NOT NULL DEFAULT 1 COMMENT '1启用 0停用',
  created_at    DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at    DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_region_code (region_code),
  KEY idx_region_parent (parent_id),
  KEY idx_region_level (region_level),
  CONSTRAINT fk_region_parent FOREIGN KEY (parent_id) REFERENCES sys_region(id)
) ENGINE=InnoDB COMMENT='行政区域表';

-- ---------------------------------------------------------------
-- 2. sys_user 用户表
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sys_user (
  id                 BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  phone              VARCHAR(20)  NOT NULL COMMENT '手机号(登录账号)',
  password_hash      VARCHAR(200) NULL COMMENT 'PBKDF2口令哈希, 验证码登录用户可为空',
  nickname           VARCHAR(64)  NOT NULL,
  avatar_url         VARCHAR(400) NULL,
  role               VARCHAR(20)  NOT NULL DEFAULT 'FARMER' COMMENT 'FARMER/BUYER/COOP/ENTERPRISE/ADMIN',
  home_region_id     BIGINT UNSIGNED NULL COMMENT '常驻区域',
  contact_display    TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否对已授权用户公开联系方式',
  contact_phone      VARCHAR(20)  NULL COMMENT '对外展示电话',
  wx_openid          VARCHAR(64)  NULL COMMENT '微信openid(可配置接入)',
  status             VARCHAR(20)  NOT NULL DEFAULT 'ACTIVE' COMMENT 'ACTIVE/FROZEN',
  last_login_at      DATETIME(3)  NULL,
  created_at         DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at         DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_user_phone (phone),
  KEY idx_user_home_region (home_region_id),
  KEY idx_user_role (role),
  CONSTRAINT fk_user_region FOREIGN KEY (home_region_id) REFERENCES sys_region(id)
) ENGINE=InnoDB COMMENT='用户表';

-- ---------------------------------------------------------------
-- 3. sys_user_role 用户角色关联表 (扩展多角色)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sys_user_role (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  user_id    BIGINT UNSIGNED NOT NULL,
  role       VARCHAR(20) NOT NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_user_role (user_id, role),
  CONSTRAINT fk_ur_user FOREIGN KEY (user_id) REFERENCES sys_user(id)
) ENGINE=InnoDB COMMENT='用户角色关联表';

-- ---------------------------------------------------------------
-- 4. merchant_profile 商家/经营主体认证
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS merchant_profile (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  user_id          BIGINT UNSIGNED NOT NULL,
  subject_type     VARCHAR(20) NOT NULL COMMENT 'PERSONAL/COOP/ENTERPRISE',
  subject_name     VARCHAR(128) NOT NULL COMMENT '主体名称/姓名',
  id_number        VARCHAR(64) NULL COMMENT '证件号(脱敏存储)',
  license_url      VARCHAR(400) NULL COMMENT '营业执照/证明材料',
  audit_status     VARCHAR(20) NOT NULL DEFAULT 'PENDING' COMMENT 'PENDING/APPROVED/REJECTED',
  audit_remark     VARCHAR(500) NULL,
  audited_by       BIGINT UNSIGNED NULL,
  audited_at       DATETIME(3) NULL,
  created_at       DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at       DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_mp_user (user_id),
  KEY idx_mp_status (audit_status),
  CONSTRAINT fk_mp_user FOREIGN KEY (user_id) REFERENCES sys_user(id)
) ENGINE=InnoDB COMMENT='商家及经营主体认证表';

-- ---------------------------------------------------------------
-- 5. product_category 农产品分类
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS product_category (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  name       VARCHAR(64) NOT NULL,
  parent_id  BIGINT UNSIGNED NULL,
  sort_order INT NOT NULL DEFAULT 0,
  is_enabled TINYINT(1) NOT NULL DEFAULT 1,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_pc_parent (parent_id),
  CONSTRAINT fk_pc_parent FOREIGN KEY (parent_id) REFERENCES product_category(id)
) ENGINE=InnoDB COMMENT='农产品分类表';

-- ---------------------------------------------------------------
-- 6. product_supply 农产品供应信息
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS product_supply (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  publisher_id    BIGINT UNSIGNED NOT NULL,
  title           VARCHAR(120) NOT NULL COMMENT '农产品名称/标题',
  category_id     BIGINT UNSIGNED NOT NULL,
  description     TEXT NULL,
  quantity        DECIMAL(12,2) NOT NULL COMMENT '供应数量',
  unit            VARCHAR(16)  NOT NULL COMMENT '计量单位: 斤/公斤/吨/箱/只...',
  price           DECIMAL(12,2) NULL COMMENT '参考单价',
  price_unit      VARCHAR(16)  NOT NULL DEFAULT '元/斤' COMMENT '价格单位',
  negotiable      TINYINT(1) NOT NULL DEFAULT 1 COMMENT '是否可议价',
  min_order_qty   DECIMAL(12,2) NULL COMMENT '最小起订量',
  province_id     BIGINT UNSIGNED NULL,
  city_id         BIGINT UNSIGNED NULL,
  county_id       BIGINT UNSIGNED NULL COMMENT '县级区域',
  town_id         BIGINT UNSIGNED NULL COMMENT '乡镇区域',
  village_id      BIGINT UNSIGNED NULL COMMENT '村级区域',
  spec            VARCHAR(200) NULL COMMENT '规格等级',
  harvest_time    VARCHAR(64)  NULL COMMENT '采收/上市时间',
  delivery_mode   VARCHAR(20) NULL COMMENT 'LOGISTICS/SELF_PICK/BOTH',
  contact_phone   VARCHAR(20) NULL,
  trade_note      VARCHAR(500) NULL COMMENT '交易说明',
  status          VARCHAR(20) NOT NULL DEFAULT 'PENDING' COMMENT 'PENDING/ACTIVE/SOLD_OUT/OFF_SHELF/EXPIRED/VIOLATION',
  effective_days  INT NOT NULL DEFAULT 30 COMMENT '有效期天数',
  expire_at       DATETIME(3) NULL,
  view_count      INT NOT NULL DEFAULT 0,
  created_at      DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at      DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_ps_publisher (publisher_id),
  KEY idx_ps_category (category_id),
  KEY idx_ps_county (county_id, status),
  KEY idx_ps_town (town_id, status),
  KEY idx_ps_village (village_id, status),
  KEY idx_ps_price (price),
  KEY idx_ps_created (created_at),
  CONSTRAINT fk_ps_user FOREIGN KEY (publisher_id) REFERENCES sys_user(id),
  CONSTRAINT fk_ps_cat FOREIGN KEY (category_id) REFERENCES product_category(id),
  CONSTRAINT fk_ps_county FOREIGN KEY (county_id) REFERENCES sys_region(id),
  CONSTRAINT fk_ps_town FOREIGN KEY (town_id) REFERENCES sys_region(id),
  CONSTRAINT fk_ps_village FOREIGN KEY (village_id) REFERENCES sys_region(id)
) ENGINE=InnoDB COMMENT='农产品供应信息表';

-- ---------------------------------------------------------------
-- 7. purchase_demand 采购需求
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS purchase_demand (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  publisher_id    BIGINT UNSIGNED NOT NULL,
  title           VARCHAR(120) NOT NULL,
  category_id     BIGINT UNSIGNED NOT NULL,
  description     TEXT NULL,
  quantity        DECIMAL(12,2) NOT NULL,
  unit            VARCHAR(16) NOT NULL,
  price_min       DECIMAL(12,2) NULL COMMENT '目标价格下限',
  price_max       DECIMAL(12,2) NULL COMMENT '目标价格上限',
  price_unit      VARCHAR(16) NOT NULL DEFAULT '元/斤',
  spec_required   VARCHAR(200) NULL COMMENT '规格质量要求',
  province_id     BIGINT UNSIGNED NULL,
  city_id         BIGINT UNSIGNED NULL,
  county_id       BIGINT UNSIGNED NULL COMMENT '采购区域-县',
  town_id         BIGINT UNSIGNED NULL COMMENT '采购区域-乡镇',
  village_id      BIGINT UNSIGNED NULL,
  expect_date     DATE NULL COMMENT '期望交货时间',
  packaging_note  VARCHAR(300) NULL COMMENT '运输包装验收要求',
  contact_name    VARCHAR(32) NULL,
  contact_phone   VARCHAR(20) NULL,
  status          VARCHAR(20) NOT NULL DEFAULT 'PENDING' COMMENT 'PENDING/ACTIVE/CLOSED/OFF_SHELF/EXPIRED/VIOLATION',
  effective_days  INT NOT NULL DEFAULT 30,
  expire_at       DATETIME(3) NULL,
  view_count      INT NOT NULL DEFAULT 0,
  created_at      DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at      DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_pd_publisher (publisher_id),
  KEY idx_pd_category (category_id),
  KEY idx_pd_county (county_id, status),
  KEY idx_pd_town (town_id, status),
  KEY idx_pd_price (price_min, price_max),
  KEY idx_pd_created (created_at),
  CONSTRAINT fk_pd_user FOREIGN KEY (publisher_id) REFERENCES sys_user(id),
  CONSTRAINT fk_pd_cat FOREIGN KEY (category_id) REFERENCES product_category(id),
  CONSTRAINT fk_pd_county FOREIGN KEY (county_id) REFERENCES sys_region(id),
  CONSTRAINT fk_pd_town FOREIGN KEY (town_id) REFERENCES sys_region(id)
) ENGINE=InnoDB COMMENT='采购需求表';

-- ---------------------------------------------------------------
-- 8. product_image 产品图片 (供应/采购通用)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS product_image (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  supply_id   BIGINT UNSIGNED NULL,
  demand_id   BIGINT UNSIGNED NULL,
  url         VARCHAR(400) NOT NULL,
  sort_order  INT NOT NULL DEFAULT 0,
  created_at  DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_pi_supply (supply_id),
  KEY idx_pi_demand (demand_id),
  CONSTRAINT fk_pi_supply FOREIGN KEY (supply_id) REFERENCES product_supply(id),
  CONSTRAINT fk_pi_demand FOREIGN KEY (demand_id) REFERENCES purchase_demand(id)
) ENGINE=InnoDB COMMENT='农产品图片表';

-- ---------------------------------------------------------------
-- 9. trade_quote 报价及协商记录
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS trade_quote (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  quote_no       VARCHAR(32) NOT NULL COMMENT '报价单号',
  supply_id      BIGINT UNSIGNED NULL COMMENT '对供应商品的报价',
  demand_id      BIGINT UNSIGNED NULL COMMENT '对采购需求的报价',
  buyer_id       BIGINT UNSIGNED NOT NULL COMMENT '买方',
  seller_id      BIGINT UNSIGNED NOT NULL COMMENT '卖方',
  initiator_id   BIGINT UNSIGNED NOT NULL COMMENT '发起方',
  price          DECIMAL(12,2) NOT NULL COMMENT '报价单价',
  price_unit     VARCHAR(16) NOT NULL,
  quantity       DECIMAL(12,2) NOT NULL COMMENT '报价数量',
  remark         VARCHAR(500) NULL COMMENT '说明: 交货时间/运输等',
  status         VARCHAR(20) NOT NULL DEFAULT 'PENDING' COMMENT 'PENDING/ACCEPTED/REJECTED/NEGOTIATED/CLOSED/DEALED',
  parent_quote_id BIGINT UNSIGNED NULL COMMENT '还价关联原报价',
  order_id       BIGINT UNSIGNED NULL COMMENT '成交后关联订单',
  created_at     DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at     DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_quote_no (quote_no),
  KEY idx_tq_supply (supply_id, status),
  KEY idx_tq_demand (demand_id, status),
  KEY idx_tq_buyer (buyer_id),
  KEY idx_tq_seller (seller_id),
  CONSTRAINT fk_tq_supply FOREIGN KEY (supply_id) REFERENCES product_supply(id),
  CONSTRAINT fk_tq_demand FOREIGN KEY (demand_id) REFERENCES purchase_demand(id),
  CONSTRAINT fk_tq_buyer FOREIGN KEY (buyer_id) REFERENCES sys_user(id),
  CONSTRAINT fk_tq_seller FOREIGN KEY (seller_id) REFERENCES sys_user(id)
) ENGINE=InnoDB COMMENT='报价及协商记录表';

-- ---------------------------------------------------------------
-- 10. trade_order 交易订单
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS trade_order (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  order_no        VARCHAR(32) NOT NULL COMMENT '订单号',
  quote_id        BIGINT UNSIGNED NOT NULL COMMENT '来源报价',
  buyer_id        BIGINT UNSIGNED NOT NULL,
  seller_id       BIGINT UNSIGNED NOT NULL,
  total_amount    DECIMAL(14,2) NOT NULL COMMENT '总金额',
  status          VARCHAR(20) NOT NULL DEFAULT 'PENDING_CONFIRM' COMMENT 'PENDING_CONFIRM/TO_PAY/TO_SHIP/SHIPPING/TO_RECEIVE/COMPLETED/CANCELLED/AFTER_SALE',
  pay_channel     VARCHAR(20) NULL COMMENT '支付渠道(预留)',
  pay_trade_no    VARCHAR(64) NULL,
  paid_at         DATETIME(3) NULL,
  shipped_at      DATETIME(3) NULL,
  completed_at    DATETIME(3) NULL,
  cancelled_at    DATETIME(3) NULL,
  cancel_reason   VARCHAR(300) NULL,
  delivery_mode   VARCHAR(20) NULL,
  address_snapshot VARCHAR(500) NULL COMMENT '交付地址快照',
  remark          VARCHAR(500) NULL,
  is_demo         TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否演示/测试数据(1=演示); 统计真实成交额时需排除',
  created_at      DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at      DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_order_no (order_no),
  KEY idx_to_quote (quote_id),
  KEY idx_to_buyer (buyer_id, status),
  KEY idx_to_seller (seller_id, status),
  KEY idx_to_status (status),
  CONSTRAINT fk_to_quote FOREIGN KEY (quote_id) REFERENCES trade_quote(id),
  CONSTRAINT fk_to_buyer FOREIGN KEY (buyer_id) REFERENCES sys_user(id),
  CONSTRAINT fk_to_seller FOREIGN KEY (seller_id) REFERENCES sys_user(id)
) ENGINE=InnoDB COMMENT='交易订单表';

-- ---------------------------------------------------------------
-- 11. trade_order_item 订单明细 (商品快照)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS trade_order_item (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  order_id       BIGINT UNSIGNED NOT NULL,
  supply_id      BIGINT UNSIGNED NULL COMMENT '原始供应ID',
  product_name   VARCHAR(120) NOT NULL COMMENT '成交时商品名快照',
  spec           VARCHAR(200) NULL COMMENT '规格快照',
  quantity       DECIMAL(12,2) NOT NULL,
  unit           VARCHAR(16) NOT NULL,
  price          DECIMAL(12,2) NOT NULL,
  price_unit     VARCHAR(16) NOT NULL,
  amount         DECIMAL(14,2) NOT NULL,
  created_at     DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_toi_order (order_id),
  CONSTRAINT fk_toi_order FOREIGN KEY (order_id) REFERENCES trade_order(id)
) ENGINE=InnoDB COMMENT='订单明细表(快照)';

-- ---------------------------------------------------------------
-- 12. trade_order_status_log 订单状态历史
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS trade_order_status_log (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  order_id      BIGINT UNSIGNED NOT NULL,
  from_status   VARCHAR(20) NULL,
  to_status    VARCHAR(20) NOT NULL,
  operator_id   BIGINT UNSIGNED NULL,
  note          VARCHAR(300) NULL,
  created_at    DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_tosl_order (order_id),
  CONSTRAINT fk_tosl_order FOREIGN KEY (order_id) REFERENCES trade_order(id)
) ENGINE=InnoDB COMMENT='订单状态流转历史';

-- ---------------------------------------------------------------
-- 13. trade_address 收货及交付地址
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS trade_address (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  user_id     BIGINT UNSIGNED NOT NULL,
  contact_name VARCHAR(32) NOT NULL,
  phone       VARCHAR(20) NOT NULL,
  province_id BIGINT UNSIGNED NULL,
  city_id     BIGINT UNSIGNED NULL,
  county_id   BIGINT UNSIGNED NULL,
  town_id     BIGINT UNSIGNED NULL,
  village_id  BIGINT UNSIGNED NULL,
  detail      VARCHAR(300) NOT NULL,
  is_default  TINYINT(1) NOT NULL DEFAULT 0,
  created_at  DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_ta_user (user_id),
  CONSTRAINT fk_ta_user FOREIGN KEY (user_id) REFERENCES sys_user(id)
) ENGINE=InnoDB COMMENT='收货及交付地址表';

-- ---------------------------------------------------------------
-- 14. user_favorite 收藏
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS user_favorite (
  id           BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  user_id      BIGINT UNSIGNED NOT NULL,
  target_type  VARCHAR(20) NOT NULL COMMENT 'SUPPLY/DEMAND',
  target_id    BIGINT UNSIGNED NOT NULL,
  created_at   DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_fav (user_id, target_type, target_id),
  CONSTRAINT fk_fav_user FOREIGN KEY (user_id) REFERENCES sys_user(id)
) ENGINE=InnoDB COMMENT='收藏表';

-- ---------------------------------------------------------------
-- 15. user_message 消息
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS user_message (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  receiver_id BIGINT UNSIGNED NOT NULL,
  sender_id   BIGINT UNSIGNED NULL COMMENT 'NULL=系统消息',
  msg_type    VARCHAR(20) NOT NULL COMMENT 'SYSTEM/QUOTE/ORDER/REPORT',
  title       VARCHAR(120) NOT NULL,
  content     VARCHAR(1000) NULL,
  biz_id      BIGINT UNSIGNED NULL COMMENT '关联业务ID(报价/订单)',
  is_read     TINYINT(1) NOT NULL DEFAULT 0,
  created_at  DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_um_receiver (receiver_id, is_read),
  KEY idx_um_created (created_at),
  CONSTRAINT fk_um_receiver FOREIGN KEY (receiver_id) REFERENCES sys_user(id)
) ENGINE=InnoDB COMMENT='站内消息表';

-- ---------------------------------------------------------------
-- 16. report_complaint 举报投诉
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS report_complaint (
  id           BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  reporter_id  BIGINT UNSIGNED NOT NULL,
  target_type  VARCHAR(20) NOT NULL COMMENT 'SUPPLY/DEMAND/USER/QUOTE',
  target_id    BIGINT UNSIGNED NOT NULL,
  reason       VARCHAR(500) NOT NULL,
  status       VARCHAR(20) NOT NULL DEFAULT 'PENDING' COMMENT 'PENDING/RESOLVED/REJECTED',
  handle_remark VARCHAR(500) NULL,
  handled_by   BIGINT UNSIGNED NULL,
  handled_at   DATETIME(3) NULL,
  created_at   DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_rc_target (target_type, target_id),
  KEY idx_rc_status (status),
  CONSTRAINT fk_rc_reporter FOREIGN KEY (reporter_id) REFERENCES sys_user(id)
) ENGINE=InnoDB COMMENT='举报投诉表';

-- ---------------------------------------------------------------
-- 17. sys_notice 平台公告
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sys_notice (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  title      VARCHAR(120) NOT NULL,
  content    TEXT NOT NULL,
  is_published TINYINT(1) NOT NULL DEFAULT 0,
  publisher_id BIGINT UNSIGNED NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id)
) ENGINE=InnoDB COMMENT='平台公告表';

-- ---------------------------------------------------------------
-- 18. sys_operation_log 后台操作日志
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sys_operation_log (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  operator_id BIGINT UNSIGNED NULL,
  module      VARCHAR(32) NOT NULL COMMENT '模块: USER/SUPPLY/DEMAND/ORDER/REGION...',
  action      VARCHAR(32) NOT NULL COMMENT '动作: AUDIT/FREEZE/ENABLE...',
  target_id   BIGINT UNSIGNED NULL,
  detail      VARCHAR(1000) NULL,
  ip          VARCHAR(64) NULL,
  created_at  DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_sol_operator (operator_id),
  KEY idx_sol_module (module),
  KEY idx_sol_created (created_at)
) ENGINE=InnoDB COMMENT='后台操作日志表';

-- ---------------------------------------------------------------
-- 19. sys_sms_code 短信验证码 (开发期模拟)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sys_sms_code (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  phone      VARCHAR(20) NOT NULL,
  code       VARCHAR(10) NOT NULL,
  purpose    VARCHAR(20) NOT NULL DEFAULT 'LOGIN',
  is_used    TINYINT(1) NOT NULL DEFAULT 0,
  expired_at DATETIME(3) NOT NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_sms_phone (phone, purpose, is_used)
) ENGINE=InnoDB COMMENT='短信验证码记录';

-- ---------------------------------------------------------------
-- 20. payment_record 支付记录 (预留, 仅真实渠道接入后启用)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS payment_record (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  order_id   BIGINT UNSIGNED NOT NULL,
  channel      VARCHAR(20) NOT NULL,
  client_token VARCHAR(64) NULL COMMENT '客户端幂等键',
  trade_no     VARCHAR(64) NULL,
  pay_url      VARCHAR(1024) NULL COMMENT '渠道返回的支付地址/二维码内容',
  amount       DECIMAL(14,2) NOT NULL,
  status       VARCHAR(20) NOT NULL DEFAULT 'INIT' COMMENT 'INIT/SUCCESS/FAILED/REFUNDED',
  refund_status VARCHAR(20) NOT NULL DEFAULT '' COMMENT '空/ PENDING / DONE',
  callback_raw TEXT NULL,
  created_at   DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_pr_order (order_id),
  -- 同一订单+渠道+幂等键只允许一条, 防止连点刷出多张可支付单
  UNIQUE KEY uk_pr_client (order_id, channel, client_token),
  KEY idx_pr_order_channel_status (order_id, channel, status),
  KEY idx_pr_refund (refund_status, created_at),
  CONSTRAINT fk_pr_order FOREIGN KEY (order_id) REFERENCES trade_order(id)
) ENGINE=InnoDB COMMENT='支付记录表(预留)';
