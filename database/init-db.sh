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

DIR="$(cd "$(dirname "$0")" && pwd)"

# 按文件名顺序执行 01..99。早期版本把文件名一个个写死, 新增迁移(13/14/15)时漏同步,
# 存量库照着 README 升级就会缺列, 支付与报警直接 1054。改成通配, 新增迁移无需再改本脚本。
SQL_FILES=$(ls "$DIR"/[0-9][0-9]_*.sql | sort)
if [ -z "$SQL_FILES" ]; then
  echo "错误: 未在 $DIR 找到迁移脚本" >&2
  exit 1
fi

i=0
total=$(echo "$SQL_FILES" | wc -l | tr -d " ")
for f in $SQL_FILES; do
  name=$(basename "$f")
  # 99_demo_seed.sql 含公开口令的测试账号, 默认不执行(mirror docker-init.sh)
  case "$name" in
    99_*)
      if [ "${SEED_DEMO:-0}" != "1" ]; then
        echo "skip $name (SEED_DEMO != 1)"
        continue
      fi
      ;;
  esac
  i=$((i+1))
  echo "[$i/$total] 执行 $name"
  "${MYSQL_CMD[@]}" < "$f"
done


echo "数据库初始化完成: agro_market"
"${MYSQL_CMD[@]}" -e "USE agro_market; SELECT region_level, COUNT(*) AS cnt FROM sys_region GROUP BY region_level ORDER BY region_level;"
