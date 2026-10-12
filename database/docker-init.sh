#!/bin/sh
# =====================================================================
# 容器内数据库初始化: 按序执行 database/0*.sql 全部迁移 (01..NN)
# 供 MySQL 官方镜像 docker-entrypoint-initdb.d 在首次启动时调用。
# - SQL 内已含 `CREATE DATABASE IF NOT EXISTS agro_market` 且各节点容器库名统一为 agro_market
# - 仅执行 0*.sql, 不会误跑 init-db.sh / 文档
# =====================================================================
set -e

DIR="/migrations"
PW="${MYSQL_ROOT_PASSWORD:-}"

if [ -z "$PW" ]; then
  echo "[docker-init] MYSQL_ROOT_PASSWORD 未设置, 跳过迁移" >&2
  exit 1
fi

for f in "$DIR"/0*.sql; do
  [ -e "$f" ] || continue
  echo "[docker-init] applying $(basename "$f")"
  mysql -uroot -p"$PW" --protocol=socket < "$f"
done

echo "[docker-init] 全部迁移完成"
mysql -uroot -p"$PW" --protocol=socket -e \
  "SELECT COUNT(*) AS federation_tables FROM information_schema.tables WHERE table_schema='agro_market' AND table_name LIKE 'fed_%';"
