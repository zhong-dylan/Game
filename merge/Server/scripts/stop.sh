#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "${SCRIPT_DIR}/common.sh"

stop_by_pid_file() {
  local pid_file="$1"
  local name="$2"

  if ! is_pid_running "${pid_file}"; then
    rm -f "${pid_file}"
    echo "${name} is not running."
    return
  fi

  local pid
  pid="$(cat "${pid_file}")"
  echo "Stopping ${name} (${pid})..."
  kill "${pid}"

  local i
  for ((i = 1; i <= 15; i++)); do
    if ! kill -0 "${pid}" >/dev/null 2>&1; then
      break
    fi
    sleep 1
  done

  rm -f "${pid_file}"
}

ensure_env

stop_by_pid_file "${NAKAMA_PID_FILE}" "Nakama"
stop_by_pid_file "${COCKROACH_PID_FILE}" "CockroachDB"
