#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
require_environment
compose up -d --build gateway
echo 'Gateway 已启动，后台地址请查看 docker compose ps（默认 http://localhost:8080/admin/）。'
