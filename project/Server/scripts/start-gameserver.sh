#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
require_environment
version="${1:-}"
if [[ ! "$version" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$ ]]; then
  echo '用法：scripts/start-gameserver.sh <版本号>，例如 1.0。' >&2
  exit 1
fi
container_name="game-${version}"
compose up -d --wait mysql redis
compose build gameserver
# A duplicate container name fails instead of replacing a running server.
compose run -d --no-deps --name "$container_name" -e "GAME_VERSION=$version" gameserver
echo "Game Server 已启动：版本 $version"
echo "在后台添加版本 ${version}，Server URL 填写 http://${container_name}:8081，再启用该版本。"
