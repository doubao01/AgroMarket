-- =====================================================================
-- AgroMarket 演示数据: 测试账号 + 示例供应/采购 (仅开发与演示环境)
--
-- !!! 生产环境禁止执行 !!!
-- 本文件里的账号口令是公开的(Abc123456), 任何读到仓库的人都能登录。
-- 早期版本把这段直接放在 02_seed.sql 里, 容器首次启动即写入生产库,
-- 等于给线上开了一个公开口令的管理员后门。
--
-- 加载方式:
--   - 本地开发: reset_db.bat 会显式执行本文件
--   - Docker:   仅当 SEED_DEMO=1 时由 docker-init.sh 执行
-- =====================================================================
USE agro_market;
-- ---------- 测试账号 (仅开发环境, 密码均为 Abc123456, 生产必须修改) ----------
-- PBKDF2-SHA256, 10000 iterations, salt = 'agromarket-dev-salt'
-- 密码哈希由后端首次启动时若表为空则自动生成; 此处为脚本内置哈希
INSERT IGNORE INTO sys_user (id, phone, password_hash, nickname, role, contact_display, contact_phone, status) VALUES
(1, '13800000001', 'PBKDF2$10000$YWdyb21hcmtldC1kZXYtc2FsdA==$IA9Q3dk6p1qSU892en8TvQhJPh2UUNADAS5wJnGtoK8=', '平台管理员', 'ADMIN', 0, NULL, 'ACTIVE'),
(2, '13800000002', 'PBKDF2$10000$YWdyb21hcmtldC1kZXYtc2FsdA==$IA9Q3dk6p1qSU892en8TvQhJPh2UUNADAS5wJnGtoK8=', '路口乡张农户', 'FARMER', 1, '13800000002', 'ACTIVE'),
(3, '13800000003', 'PBKDF2$10000$YWdyb21hcmtldC1kZXYtc2FsdA==$IA9Q3dk6p1qSU892en8TvQhJPh2UUNADAS5wJnGtoK8=', '包信镇王收购', 'BUYER', 1, '13800000003', 'ACTIVE'),
(4, '13800000004', 'PBKDF2$10000$YWdyb21hcmtldC1kZXYtc2FsdA==$IA9Q3dk6p1qSU892en8TvQhJPh2UUNADAS5wJnGtoK8=', '息县农业合作社', 'COOP', 1, '13800000004', 'ACTIVE');

INSERT IGNORE INTO sys_user_role (user_id, role) VALUES
(1,'ADMIN'),(2,'FARMER'),(3,'BUYER'),(4,'COOP');

-- 守卫: 若 2/3/4 号 id 已被别的手机号占用(在非空库上误跑本文件), 立即报错终止,
-- 绝不去改真实用户的常驻区域。子查询返回多行是刻意的报错手段。
SET @guard = (
  SELECT IF(COUNT(*) = 0, 0, (SELECT 1 UNION SELECT 2))
  FROM sys_user
  WHERE (id = 2 AND phone <> '13800000002')
     OR (id = 3 AND phone <> '13800000003')
     OR (id = 4 AND phone <> '13800000004')
);

-- 用户常驻区域(只动演示账号本身)
UPDATE sys_user SET home_region_id = 31 WHERE id = 2 AND phone = '13800000002';
UPDATE sys_user SET home_region_id = 20 WHERE id = 3 AND phone = '13800000003';
UPDATE sys_user SET home_region_id = 3  WHERE id = 4 AND phone = '13800000004';

-- ---------- 演示供应/采购数据 (标记为演示数据, 非真实交易) ----------
INSERT IGNORE INTO product_supply (id, publisher_id, title, category_id, description, quantity, unit, price, price_unit, negotiable, min_order_qty, county_id, town_id, village_id, spec, harvest_time, delivery_mode, contact_phone, status, effective_days) VALUES
(1, 2, '优质小麦 新收上市', 101, '自家种植优质小麦，容重高、水分达标，适合面粉厂收购。', 50000.00, '斤', 1.28, '元/斤', 1, 1000.00, 3, 31, NULL, '二级以上 水分≤12.5%', '2026-06 收获', 'LOGISTICS', '13800000002', 'ACTIVE', 90),
(2, 2, '路口西瓜 沙瓤甜瓜', 501, '路口乡特产西瓜，沙瓤多汁，糖度高，现摘现发。', 8000.00, '斤', 1.50, '元/斤', 1, 200.00, 3, 31, NULL, '单果 8-12 斤', '2026-07 上市', 'BOTH', '13800000002', 'ACTIVE', 60),
(3, 4, '高台阶花生 果粒饱满', 601, '合作社统一收购社员花生，果白粒饱，出油率高。', 20000.00, '斤', 3.80, '元/斤', 0, 500.00, 3, 23, NULL, '白沙系列 霉变率≤1%', '2026-10 新货', 'BOTH', '13800000004', 'ACTIVE', 120);

INSERT IGNORE INTO purchase_demand (id, publisher_id, title, category_id, description, quantity, unit, price_min, price_max, price_unit, spec_required, county_id, town_id, packaging_note, contact_name, contact_phone, status, effective_days) VALUES
(1, 3, '长期收购小麦 现金结算', 101, '粮点常年收购小麦，量大价优，现金结算，可上门验货。', 200000.00, '斤', 1.25, 1.35, '元/斤', '容重≥770g/L 水分≤12.5%', 3, 20, '散装或编织袋均可', '王收购', '13800000003', 'ACTIVE', 180),
(2, 4, '合作社收购红薯 淀粉加工用', 104, '淀粉加工厂订单收购红薯，要求无霉变、无严重虫眼。', 100000.00, '斤', 0.60, 0.80, '元/斤', '商薯19 徐薯22 为主', 3, NULL, '网袋包装', '李经理', '13800000004', 'ACTIVE', 90);
