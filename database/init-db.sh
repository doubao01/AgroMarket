#!/usr/bin/env bash
# =====================================================================
# AgroMarket 数据库初始化脚本 (可重复执行)
# 用法: ./database/init-db.sh
# 默认连接本机 root 无密码 (开发环境); 可通过环境变量覆盖
# =====================================================================
set -e
DB_HOST="${DB_HOST:-127.0.0.1}"
DB_PORT="${DB_PORT:-3306}"
DB_USER="${DB_USER:-root}"
DB_PASS="${DB_PASS:-${DB_PASSWORD:-}}"

# 兼容 MariaDB / MySQL 客户端
if command -v mariadb >/dev/null 2>&1; then
  MYSQL_BIN=mariadb
elif command -v mysql >/dev/null 2>&1; then
  MYSQL_BIN=mysql
else
  echo "错误: 未找到 mariadb 或 mysql 客户端, 请先安装其中之一" >&2
  exit 1
fi

MYSQL_CMD=("$MYSQL_BIN" -h"$DB_HOST" -P"$DB_PORT" -u"$DB_USER")
if [ -n "$DB_PASS" ]; then
  MYSQL_CMD+=(-p"$DB_PASS")
fi

echo "[1/11] 执行表结构脚本 01_schema.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/01_schema.sql"

echo "[2/11] 执行种子数据脚本 02_seed.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/02_seed.sql"

echo "[3/11] 执行聊天模块迁移 03_chat.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/03_chat.sql"

echo "[4/11] 执行聊天增强迁移 04_chat_wechat.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/04_chat_wechat.sql"

echo "[5/11] 执行 AI 对话迁移 05_ai_chat.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/05_ai_chat.sql"

echo "[6/11] 执行聊天维护迁移 06_chat_cleanup.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/06_chat_cleanup.sql"

echo "[7/11] 执行节点联动迁移 07_federation.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/07_federation.sql"

echo "[8/11] 执行节点联动非对称密钥迁移 08_federation_keys.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/08_federation_keys.sql"

echo "[9/11] 执行节点联动运行时设置迁移 09_federation_settings.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/09_federation_settings.sql"

echo "[10/11] 执行市场增强迁移 10_market_enhance.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/10_market_enhance.sql"

echo "[11/12] 执行消息推送与报警迁移 11_push_alert.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/11_push_alert.sql"

echo "[12/12] 执行社区与焦点图迁移 12_community.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/12_community.sql"

echo "数据库初始化完成: agro_market"
"${MYSQL_CMD[@]}" -e "USE agro_market; SELECT region_level, COUNT(*) AS cnt FROM sys_region GROUP BY region_level ORDER BY region_level;"
