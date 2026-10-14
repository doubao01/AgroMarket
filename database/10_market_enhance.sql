-- =============================================================
-- 10. 市场增强: 采购需求可议价开关 + 采购已售统计列
-- 兼容 MySQL 8 / MariaDB, 幂等可重复执行
-- =============================================================
USE agro_market;

-- 采购需求"价格可议"开关(与供应 negotiable 对齐, 默认 true)
SET @c := (SELECT COUNT(*) FROM information_schema.columns
          WHERE table_schema=DATABASE() AND table_name='purchase_demand' AND column_name='negotiable');
SET @s := IF(@c=0, 'ALTER TABLE purchase_demand ADD COLUMN negotiable TINYINT(1) NOT NULL DEFAULT 1 COMMENT ''价格可议''',
             'DO 0');
PREPARE _st FROM @s; EXECUTE _st; DEALLOCATE PREPARE _st;
