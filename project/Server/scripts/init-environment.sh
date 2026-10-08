#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
if [[ -e "$SERVER_DIR/.env" ]]; then
  echo '.env 已存在，保留现有配置。修改配置请编辑 Server/.env。'
  exit 0
fi
command -v openssl >/dev/null || { echo '需要 openssl 生成数据库密码。' >&2; exit 1; }
admin_username="${ADMIN_USERNAME:-admin}"
admin_password="${ADMIN_PASSWORD:-}"
if [[ -z "$admin_password" ]]; then
  if [[ ! -t 0 ]]; then echo '非交互执行时请通过 ADMIN_PASSWORD 提供 12–72 位管理员密码。' >&2; exit 1; fi
  read -r -s -p '管理员密码（12–72 位，不显示输入）：' admin_password
  echo
  read -r -s -p '再次输入密码：' password_confirmation
  echo
  [[ "$admin_password" == "$password_confirmation" ]] || { echo '两次密码不一致。' >&2; exit 1; }
fi
# Keep .env quoting unambiguous; generated database passwords are hexadecimal.
if [[ ! "$admin_username" =~ ^[A-Za-z0-9_.-]{1,64}$ ]]; then echo '管理员账户需为 1–64 个字母、数字、点、下划线或连字符。' >&2; exit 1; fi
if (( ${#admin_password} < 12 || ${#admin_password} > 72 )) || [[ "$admin_password" == *"'"* || "$admin_password" == *\\* || "$admin_password" == *$'\n'* || "$admin_password" == *$'\r'* ]]; then
  echo '管理员密码需为 12–72 位，不含单引号、反斜杠或换行。' >&2; exit 1
fi
umask 077
# noclobber also prevents a concurrent invocation from overwriting the file.
set -o noclobber
{
  printf 'MYSQL_ROOT_PASSWORD=%s\n' "$(openssl rand -hex 24)"
  printf 'MYSQL_PASSWORD=%s\n' "$(openssl rand -hex 24)"
  printf 'REDIS_PASSWORD=%s\n' "$(openssl rand -hex 24)"
  printf 'ADMIN_USERNAME=%s\n' "$admin_username"
  printf "ADMIN_PASSWORD='%s'\n" "$admin_password"
  printf 'ADMIN_COOKIE_SECURE=false\nGATEWAY_BIND=127.0.0.1\nGATEWAY_PORT=8080\n'
} > "$SERVER_DIR/.env"
echo '已创建 Server/.env（仅当前用户可读写）。接下来运行 scripts/init-db.sh。'
