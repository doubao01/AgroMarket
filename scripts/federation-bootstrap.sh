#!/bin/sh
# =====================================================================
# 联动节点引导：等待各节点就绪 -> 每个节点互相登记其余节点为对端 -> 触发首轮推/拉
#
# 依赖: curl + POSIX sh（容器用 curlimages/curl；本机需装 curl）
#
# 环境变量:
#   FED_ALL        必填。节点描述列表, 用 ; 分隔, 每项格式:
#                    nodeCode|baseUrl|level|name[|regionCode]
#                  baseUrl 必须是"从当前执行环境可达"的地址:
#                    - 容器内:   http://api-henan:8080
#                    - 本机演示: http://127.0.0.1:8090
#   FED_ADMIN_PHONE / FED_ADMIN_PASSWORD   管理员登录凭据（默认种子值）
#   FED_SYNC_ON_BOOTSTRAP                  true/false, 默认 true, 登记完触发一次推+拉
#   FED_WAIT_RETRIES                       单节点健康检查重试次数, 默认 60（每次 2s）
#
# 示例:
#   FED_ALL="node-a|http://127.0.0.1:8090|COUNTY|A县|411528;node-b|http://127.0.0.1:8091|COUNTY|B县|411626" \
#   sh scripts/federation-bootstrap.sh
# =====================================================================
set -eu

: "${FED_ALL:?FED_ALL 必填，格式 nodeCode|baseUrl|level|name[|regionCode] 以 ; 分隔}"
PHONE="${FED_ADMIN_PHONE:-13800000001}"
PASSWORD="${FED_ADMIN_PASSWORD:-Abc123456}"
SYNC="${FED_SYNC_ON_BOOTSTRAP:-true}"
RETRIES="${FED_WAIT_RETRIES:-60}"

# 从扁平 JSON 里取第一个字符串字段值（令牌/ID）
json_str() { printf '%s' "$1" | sed -n "s/.*\"$2\":\"\([^\"]*\)\".*/\1/p" | head -n 1; }

wait_up() {
  i=0
  while [ "$i" -lt "$RETRIES" ]; do
    if curl -fsS "$1/health" >/dev/null 2>&1; then
      echo "[bootstrap] 节点就绪: $1"
      return 0
    fi
    i=$((i + 1))
    sleep 2
  done
  echo "[bootstrap] 节点未就绪(超时): $1" >&2
  return 1
}

login_token() {
  resp=$(curl -fsS -X POST "$1/api/auth/login/password" \
    -H 'Content-Type: application/json' \
    -d "{\"phone\":\"$PHONE\",\"password\":\"$PASSWORD\"}")
  tok=$(json_str "$resp" token)
  [ -n "$tok" ] || { echo "[bootstrap] 登录失败: $1 => $resp" >&2; return 1; }
  printf '%s' "$tok"
}

field() { printf '%s' "$1" | cut -d'|' -f"$2"; }

# 注意: 必须带结尾换行, 否则 read 会丢弃最后一条（无分隔符时返回非零）
NODES=$(printf '%s\n' "$FED_ALL" | tr ';' '\n')

# 逐节点执行
printf '%s\n' "$NODES" | while IFS= read -r line; do
  [ -n "$line" ] || continue
  self_code=$(field "$line" 1)
  self_url=$(field "$line" 2)

  wait_up "$self_url"
  token=$(login_token "$self_url")
  echo "[bootstrap] [$self_code] 已登录, 开始登记对端"

  printf '%s\n' "$NODES" | while IFS= read -r peer; do
    [ -n "$peer" ] || continue
    peer_code=$(field "$peer" 1)
    peer_url=$(field "$peer" 2)
    peer_level=$(field "$peer" 3)
    peer_name=$(field "$peer" 4)
    peer_region=$(field "$peer" 5)
    [ "$peer_code" = "$self_code" ] && continue

    if [ -n "$peer_region" ] && [ "$peer_region" != "$peer" ]; then
      region_json="\"$peer_region\""
    else
      region_json="null"
    fi

    body="{\"nodeCode\":\"$peer_code\",\"nodeName\":\"$peer_name\",\"level\":\"$peer_level\",\"regionCode\":$region_json,\"baseUrl\":\"$peer_url\",\"nodeToken\":null,\"direction\":\"BOTH\",\"status\":\"ACTIVE\"}"
    code=$(curl -sS -o /tmp/fed_resp.$$ -w '%{http_code}' -X POST "$self_url/api/admin/federation/nodes" \
      -H "Authorization: Bearer $token" -H 'Content-Type: application/json' -d "$body")
    if [ "$code" = "200" ]; then
      echo "[bootstrap]   [$self_code] -> 登记对端 $peer_code ($peer_url)"
    else
      echo "[bootstrap]   [$self_code] -> 登记对端 $peer_code 失败 HTTP $code: $(cat /tmp/fed_resp.$$)" >&2
    fi
  done

  if [ "$SYNC" = "true" ]; then
    echo "[bootstrap] [$self_code] 触发首轮同步"
    curl -fsS -X POST "$self_url/api/admin/federation/sync/pull" -H "Authorization: Bearer $token" >/dev/null || true
    curl -fsS -X POST "$self_url/api/admin/federation/sync/push" -H "Authorization: Bearer $token" >/dev/null || true
    agg=$(curl -fsS "$self_url/api/admin/federation/aggregate?range=30d" -H "Authorization: Bearer $token" || true)
    echo "[bootstrap]   [$self_code] 汇聚: $(printf '%s' "$agg" | sed -n 's/.*"data":{\(.*\)}.*/\1/p' | head -c 400)"
  fi
done

echo "[bootstrap] 完成"
