#!/usr/bin/env bash
# 把已存在的商家证件材料从公开目录搬到私有目录, 并把库里地址改成 /api/files/private/...
#
# 为什么需要单独跑: 代码改动只保护"新上传"的文件。历史上传的证件仍在
# uploads/<date>/ 下, 静态服务会继续无条件提供 —— 漏洞依旧存在, 直到这些文件被搬走。
#
# 用法:
#   ./scripts/migrate-private-uploads.sh            # 预演, 只打印将要做什么
#   ./scripts/migrate-private-uploads.sh --apply    # 真正执行
#
# 依赖: mysql 客户端与 mysqldump 同版本; 在 backend/AgroMarket.Api 目录下执行。
set -euo pipefail

APPLY=0
[ "${1:-}" = "--apply" ] && APPLY=1

UPLOADS="${UPLOADS:-./uploads}"
: "${DB_HOST:=127.0.0.1}"
: "${DB_PORT:=3306}"
: "${DB_USER:=root}"
: "${DB_NAME:=agro_market}"

query() { mysql -h"$DB_HOST" -P"$DB_PORT" -u"$DB_USER" -N -B -e "USE $DB_NAME; $1"; }

echo "== 收集引用中的证件地址"
mapfile -t URLS < <(query "SELECT DISTINCT license_url FROM merchant_profile WHERE license_url IS NOT NULL AND license_url <> ''")

moved=0
for url in "${URLS[@]}"; do
  case "$url" in
    /uploads/*) ;;
    /api/files/private/*) echo "  已是私有地址, 跳过: $url"; continue ;;
    *) echo "  非本站地址, 跳过(需人工确认来源): $url"; continue ;;
  esac

  src="$UPLOADS/${url#/uploads/}"
  rel="${url#/uploads/}"
  dst="$UPLOADS/.private/$rel"

  if [ ! -f "$src" ]; then echo "  源文件不存在: $src"; continue; fi

  echo "  $url"
  echo "    mv $src -> $dst"
  echo "    UPDATE merchant_profile / sys_upload"
  if [ "$APPLY" = "1" ]; then
    mkdir -p "$(dirname "$dst")"
    mv "$src" "$dst"
    newurl="/api/files/private/$rel"
    query "UPDATE merchant_profile SET license_url='$newurl' WHERE license_url='$url';
           UPDATE sys_upload SET url='$newurl', is_private=1 WHERE url='$url';" >/dev/null
  fi
  moved=$((moved+1))
done

echo
if [ "$APPLY" = "1" ]; then
  echo "完成: 已搬迁 $moved 个证件文件。"
else
  echo "预演结束: 将搬迁 $moved 个证件文件。确认无误后加 --apply 执行。"
fi
