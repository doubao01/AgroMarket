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
DB_PASS="${DB_PASS:-}"

MYSQL_CMD=(mariadb -h"$DB_HOST" -P"$DB_PORT" -u"$DB_USER")
if [ -n "$DB_PASS" ]; then
  MYSQL_CMD+=(-p"$DB_PASS")
fi

echo "[1/6] 执行表结构脚本 01_schema.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/01_schema.sql"

echo "[2/6] 执行种子数据脚本 02_seed.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/02_seed.sql"

echo "[3/6] 执行聊天模块迁移 03_chat.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/03_chat.sql"

echo "[4/6] 执行聊天增强迁移 04_chat_wechat.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/04_chat_wechat.sql"

echo "[5/6] 执行 AI 对话迁移 05_ai_chat.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/05_ai_chat.sql"

echo "[6/6] 执行聊天维护迁移 06_chat_cleanup.sql"
"${MYSQL_CMD[@]}" < "$(dirname "$0")/06_chat_cleanup.sql"

echo "数据库初始化完成: agro_market"
"${MYSQL_CMD[@]}" -e "USE agro_market; SELECT region_level, COUNT(*) AS cnt FROM sys_region GROUP BY region_level ORDER BY region_level;"
