-- =====================================================================
-- AgroMarket 节点联动 非对称令牌密钥迁移 (08)
-- 令牌双模式: SYMMETRIC(HS256 共享密钥, 默认) / ASYMMETRIC(RS256 每节点密钥对)
--   - 本节点密钥对存于 fed_keypair (单行 id=1)
--   - 对端公钥拉取后存于 fed_node.public_key, 用于校验 RS256 节点令牌
-- 可重复执行: CREATE IF NOT EXISTS; 追加列用 information_schema 判定的动态 ALTER (兼容 MySQL 8 与 MariaDB)
-- =====================================================================

USE agro_market;

-- ---------------------------------------------------------------
-- F6. fed_keypair 本节点非对称令牌密钥对 (单行, id 恒为 1)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS fed_keypair (
  id          TINYINT UNSIGNED NOT NULL DEFAULT 1,
  node_code   VARCHAR(64)  NOT NULL COMMENT '归属节点编码',
  alg         VARCHAR(16)  NOT NULL DEFAULT 'RS256' COMMENT '签名算法',
  public_key  TEXT         NOT NULL COMMENT '公钥(PEM, SPKI)',
  private_key TEXT         NOT NULL COMMENT '私钥(PEM, PKCS8) — 仅本节点持有',
  created_at  DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at  DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id)
) ENGINE=InnoDB COMMENT='本节点非对称令牌密钥对(单行)';

-- ---------------------------------------------------------------
-- fed_node 追加对端公钥列
-- ---------------------------------------------------------------
-- fed_node 追加对端公钥列 (MySQL 8 与 MariaDB 的 IF NOT EXISTS 语法不通用, 用 information_schema 判定)
SET @c := (SELECT COUNT(*) FROM information_schema.columns
           WHERE table_schema=DATABASE() AND table_name='fed_node' AND column_name='public_key');
SET @s := IF(@c=0,
  'ALTER TABLE fed_node ADD COLUMN public_key TEXT NULL COMMENT ''对端公钥(PEM, 用于校验 RS256 令牌)'', ADD COLUMN key_fetched_at DATETIME(3) NULL COMMENT ''公钥拉取时间''',
  'DO 0');
PREPARE _st FROM @s; EXECUTE _st; DEALLOCATE PREPARE _st;
