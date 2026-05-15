#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "${SCRIPT_DIR}/common.sh"

ensure_dirs

case "${1:-all}" in
  cockroach)
    tail -f "${COCKROACH_LOG_FILE}"
    ;;
  nakama)
    tail -f "${NAKAMA_LOG_FILE}"
    ;;
  all)
    touch "${COCKROACH_LOG_FILE}" "${NAKAMA_LOG_FILE}"
    tail -f "${COCKROACH_LOG_FILE}" "${NAKAMA_LOG_FILE}"
    ;;
  *)
    echo "Usage: $0 [all|cockroach|nakama]"
    exit 1
    ;;
esac
