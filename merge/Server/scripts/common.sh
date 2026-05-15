#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
ENV_FILE="${SERVER_DIR}/.env"
ENV_EXAMPLE_FILE="${SERVER_DIR}/.env.example"
RUN_DIR="${SERVER_DIR}/run"
LOG_DIR="${SERVER_DIR}/logs"
DATA_DIR="${SERVER_DIR}/data"
COCKROACH_DATA_DIR="${DATA_DIR}/cockroach"
NAKAMA_DATA_DIR="${DATA_DIR}/nakama"
COCKROACH_PID_FILE="${RUN_DIR}/cockroach.pid"
NAKAMA_PID_FILE="${RUN_DIR}/nakama.pid"
COCKROACH_LOG_FILE="${LOG_DIR}/cockroach.log"
NAKAMA_LOG_FILE="${LOG_DIR}/nakama.log"

ensure_env() {
  if [[ ! -f "${ENV_FILE}" ]]; then
    cp "${ENV_EXAMPLE_FILE}" "${ENV_FILE}"
    echo "Created ${ENV_FILE} from .env.example. Update paths if needed."
  fi

  # shellcheck disable=SC1090
  source "${ENV_FILE}"
}

ensure_dirs() {
  mkdir -p "${RUN_DIR}" "${LOG_DIR}" "${COCKROACH_DATA_DIR}" "${NAKAMA_DATA_DIR}"
}

find_nakama_binary() {
  if [[ -n "${NAKAMA_BINARY:-}" ]]; then
    echo "${NAKAMA_BINARY}"
    return
  fi

  local candidate
  candidate="$(find "${SERVER_DIR}" -maxdepth 2 -type f -name nakama | head -n 1 || true)"
  if [[ -n "${candidate}" ]]; then
    echo "${candidate}"
    return
  fi

  echo ""
}

find_cockroach_binary() {
  if [[ -n "${COCKROACH_BINARY:-}" ]]; then
    echo "${COCKROACH_BINARY}"
    return
  fi

  local candidate
  candidate="$(find "${SERVER_DIR}" -maxdepth 3 -type f -name cockroach | head -n 1 || true)"
  if [[ -n "${candidate}" ]]; then
    echo "${candidate}"
    return
  fi

  echo ""
}

require_binary() {
  local path="$1"
  local name="$2"
  if [[ -z "${path}" || ! -x "${path}" ]]; then
    echo "Missing executable for ${name}: ${path:-not found}"
    if [[ "${name}" == "cockroach" ]]; then
      echo "Run ./scripts/install-cockroach.sh first, or set COCKROACH_BINARY in Server/.env"
    fi
    exit 1
  fi
}

is_pid_running() {
  local pid_file="$1"
  if [[ ! -f "${pid_file}" ]]; then
    return 1
  fi

  local pid
  pid="$(cat "${pid_file}")"
  if [[ -z "${pid}" ]]; then
    return 1
  fi

  kill -0 "${pid}" >/dev/null 2>&1
}

wait_for_port() {
  local host="$1"
  local port="$2"
  local retries="${3:-30}"

  local i
  for ((i = 1; i <= retries; i++)); do
    if nc -z "${host}" "${port}" >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done

  return 1
}
