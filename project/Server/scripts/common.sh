#!/usr/bin/env bash
set -euo pipefail
SERVER_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
require_environment() {
  if [[ ! -f "$SERVER_DIR/.env" ]]; then
    echo '请先运行 scripts/init-environment.sh 创建环境配置。' >&2
    exit 1
  fi
}
compose() { docker compose --project-directory "$SERVER_DIR" --env-file "$SERVER_DIR/.env" -f "$SERVER_DIR/compose.yaml" "$@"; }
