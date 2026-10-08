#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
require_environment
compose up -d --wait mysql redis
compose build db-init
compose run --rm --no-deps db-init
echo 'MySQL 表和首个管理员已初始化；现有账户、版本路由均保留。Redis 已就绪，不写入默认版本。'
