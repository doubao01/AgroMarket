#!/bin/sh
# =====================================================================
# 两节点联动实景演示（本机、非 Docker）
#   - 建两个独立库 (agro_market_demo_a / _b)，各自跑一套 API
#   - 用 scripts/federation-bootstrap.sh 互相登记对端并首轮同步
#   - 打印两节点各自的跨节点汇聚结果，验证扁平联邦数据互通
#
# 用法:
#   sh scripts/federation-demo.sh          # 启动并跑完整演示（进程保留运行）
#   sh scripts/federation-demo.sh stop     # 停止演示实例
#   sh scripts/federation-demo.sh verify   # 仅对已运行的实例复查汇聚
# =====================================================================
set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DLL="$ROOT/backend/AgroMarket.Api/bin/Debug/net10.0/AgroMarket.Api.dll"

DB_A="agro_market_demo_a"; PORT_A=8090; CODE_A="node-xixian";   NAME_A="息县农贸通";   LEVEL_A="COUNTY"; REGION_A="411528"
DB_B="agro_market_demo_b"; PORT_B=8091; CODE_B="node-huaiyang"; NAME_B="淮阳县农贸通"; LEVEL_B="COUNTY"; REGION_B="411626"

SHARED_KEY="demo-federation-shared-key-at-least-32-bytes-000"
ADMIN_PHONE="13800000001"; ADMIN_PASSWORD="Abc123456"
APP_USER="agromarket"; APP_PASS="CHANGE_ME_DB_PASSWORD"
LOG_A="/tmp/fed-demo-a.log"; LOG_B="/tmp/fed-demo-b.log"
PID_A="/tmp/fed-demo-a.pid"; PID_B="/tmp/fed-demo-b.pid"

FED_ALL="${CODE_A}|http://127.0.0.1:${PORT_A}|${LEVEL_A}|${NAME_A}|${REGION_A};${CODE_B}|http://127.0.0.1:${PORT_B}|${LEVEL_B}|${NAME_B}|${REGION_B}"

login_token() { # $1=port
  curl -fsS -X POST "http://127.0.0.1:$1/api/auth/login/password" \
    -H 'Content-Type: application/json' \
    -d "{\"phone\":\"$ADMIN_PHONE\",\"password\":\"$ADMIN_PASSWORD\"}" \
    | sed -n 's/.*"token":"\([^"]*\)".*/\1/p' | head -n1
}

wait_health() { # $1=port $2=log
  i=0
  while [ "$i" -lt 90 ]; do
    if curl -fsS "http://127.0.0.1:$1/health" >/dev/null 2>&1; then
      echo "[demo] 端口 $1 就绪"
      return 0
    fi
    i=$((i + 1)); sleep 1
  done
  echo "[demo] 端口 $1 未就绪，日志尾部:" >&2
  tail -30 "$2" >&2 || true
  return 1
}

stop_all() {
  for p in "$PID_A" "$PID_B"; do
    if [ -f "$p" ]; then
      pid=$(cat "$p")
      kill "$pid" 2>/dev/null || true
      rm -f "$p"
      echo "[demo] 已停止 PID $pid"
    fi
  done
}

show_aggregate() { # $1=port $2=label
  tok=$(login_token "$1")
  echo "----- [$2] 跨节点汇聚 (range=30d) -----"
  curl -fsS "http://127.0.0.1:$1/api/admin/federation/aggregate?range=30d" \
    -H "Authorization: Bearer $tok"
  echo
  echo "----- [$2] 已登记对端 -----"
  curl -fsS "http://127.0.0.1:$1/api/admin/federation/nodes" \
    -H "Authorization: Bearer $tok" | sed 's/,"/,\n  "/g'
  echo
}

case "${1:-run}" in
  stop)
    stop_all
    exit 0
    ;;
  verify)
    show_aggregate "$PORT_A" "A"
    show_aggregate "$PORT_B" "B"
    exit 0
    ;;
esac

# ---------- 0. 前置检查 ----------
[ -f "$DLL" ] || { echo "[demo] 未找到构建产物，先构建..."; (cd "$ROOT/backend" && dotnet build AgroMarket.Api/AgroMarket.Api.csproj -v q); }

command -v mysql >/dev/null || { echo "[demo] 需要 mysql 客户端" >&2; exit 1; }

echo "[demo] 清理可能残留的演示实例"
stop_all

# ---------- 1. 重建两个独立库 ----------
init_db() { # $1=dbname
  mysql -uroot -e "DROP DATABASE IF EXISTS $1; CREATE DATABASE $1 CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
  mysql -uroot -e "GRANT ALL PRIVILEGES ON $1.* TO 'agromarket'@'localhost'; GRANT ALL PRIVILEGES ON $1.* TO 'agromarket'@'127.0.0.1'; GRANT ALL PRIVILEGES ON $1.* TO 'agromarket'@'%'; FLUSH PRIVILEGES;"
  for f in "$ROOT"/database/0*.sql; do
    sed "s/agro_market/$1/g" "$f" | mysql -uroot
  done
  cnt=$(mysql -uroot -N -B -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='$1';")
  echo "[demo] $1 初始化完成 (表数: $cnt)"
}

init_db "$DB_A"
init_db "$DB_B"

# ---------- 2. 启动两个 API 实例 ----------
cd "$ROOT/backend/AgroMarket.Api"

ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="http://127.0.0.1:$PORT_A" \
ConnectionStrings__Default="Server=127.0.0.1;Port=3306;Database=$DB_A;User=$APP_USER;Password=$APP_PASS;CharSet=utf8mb4;" \
Federation__Enabled=true Federation__NodeCode="$CODE_A" Federation__NodeName="$NAME_A" \
Federation__Level="$LEVEL_A" Federation__RegionCode="$REGION_A" Federation__SharedKey="$SHARED_KEY" \
Federation__AutoSync=true Federation__SyncIntervalMinutes=60 \
dotnet "$DLL" >"$LOG_A" 2>&1 &
echo $! > "$PID_A"

ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="http://127.0.0.1:$PORT_B" \
ConnectionStrings__Default="Server=127.0.0.1;Port=3306;Database=$DB_B;User=$APP_USER;Password=$APP_PASS;CharSet=utf8mb4;" \
Federation__Enabled=true Federation__NodeCode="$CODE_B" Federation__NodeName="$NAME_B" \
Federation__Level="$LEVEL_B" Federation__RegionCode="$REGION_B" Federation__SharedKey="$SHARED_KEY" \
Federation__AutoSync=true Federation__SyncIntervalMinutes=60 \
dotnet "$DLL" >"$LOG_B" 2>&1 &
echo $! > "$PID_B"

echo "[demo] A pid=$(cat "$PID_A")  B pid=$(cat "$PID_B")"
wait_health "$PORT_A" "$LOG_A"
wait_health "$PORT_B" "$LOG_B"

# ---------- 3. 引导：互相登记对端 + 首轮同步 ----------
echo "[demo] 引导联邦..."
FED_ALL="$FED_ALL" \
FED_ADMIN_PHONE="$ADMIN_PHONE" FED_ADMIN_PASSWORD="$ADMIN_PASSWORD" \
FED_SYNC_ON_BOOTSTRAP=true \
sh "$ROOT/scripts/federation-bootstrap.sh"

# ---------- 4. 验证 ----------
echo
echo "==================== 验证 ===================="
for pair in "$PORT_A:A:$CODE_B" "$PORT_B:B:$CODE_A"; do
  port=${pair%%:*}; rest=${pair#*:}; label=${rest%%:*}; peer=${rest#*:}
  agg=$(curl -fsS "http://127.0.0.1:$port/api/admin/federation/aggregate?range=30d" \
    -H "Authorization: Bearer $(login_token "$port")")
  if printf '%s' "$agg" | grep -q "\"sourceNodeCode\":\"$peer\""; then
    echo "[PASS] 节点 $label 的汇聚数据已包含对端来源 $peer"
  else
    echo "[FAIL] 节点 $label 未汇聚到对端 $peer 的数据" >&2
    echo "       $agg" >&2
  fi
done

echo
show_aggregate "$PORT_A" "A"
show_aggregate "$PORT_B" "B"

echo "[demo] 完成。停止: sh scripts/federation-demo.sh stop"
