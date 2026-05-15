#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "${SCRIPT_DIR}/common.sh"

ensure_env
ensure_dirs

NAKAMA_BIN="$(find_nakama_binary)"
COCKROACH_BIN="$(find_cockroach_binary)"

require_binary "${NAKAMA_BIN}" "nakama"
require_binary "${COCKROACH_BIN}" "cockroach"

if ! is_pid_running "${COCKROACH_PID_FILE}"; then
  echo "Starting CockroachDB..."
  cockroach_args=(
    start-single-node
    "--listen-addr=${COCKROACH_HOST}:${COCKROACH_SQL_PORT}"
    "--http-addr=${COCKROACH_HOST}:${COCKROACH_HTTP_PORT}"
    "--store=${COCKROACH_DATA_DIR}"
    "--cache=${COCKROACH_CACHE}"
    "--max-sql-memory=${COCKROACH_MAX_SQL_MEMORY}"
    "--pid-file=${COCKROACH_PID_FILE}"
  )

  if [[ "${COCKROACH_INSECURE}" == "true" ]]; then
    cockroach_args+=(--insecure)
  fi

  "${COCKROACH_BIN}" "${cockroach_args[@]}" \
    >> "${COCKROACH_LOG_FILE}" 2>&1 &

  if ! wait_for_port "${COCKROACH_HOST}" "${COCKROACH_SQL_PORT}" 30; then
    echo "CockroachDB did not become ready. Check ${COCKROACH_LOG_FILE}"
    exit 1
  fi
else
  echo "CockroachDB is already running."
fi

echo "Running Nakama migrations..."
"${NAKAMA_BIN}" migrate up --database.address "${NAKAMA_DATABASE_ADDRESS}"

if is_pid_running "${NAKAMA_PID_FILE}"; then
  echo "Nakama is already running."
  exit 0
fi

echo "Starting Nakama..."
nohup "${NAKAMA_BIN}" \
  --name "${NAKAMA_NODE_NAME}" \
  --data_dir "${NAKAMA_DATA_DIR}" \
  --database.address "${NAKAMA_DATABASE_ADDRESS}" \
  --logger.level "${NAKAMA_LOG_LEVEL}" \
  --session.token_expiry_sec "${NAKAMA_SESSION_TOKEN_EXPIRY_SEC}" \
  --socket.port "${NAKAMA_HTTP_PORT}" \
  --console.port "${NAKAMA_CONSOLE_PORT}" \
  --console.username "${NAKAMA_CONSOLE_USERNAME}" \
  --console.password "${NAKAMA_CONSOLE_PASSWORD}" \
  >> "${NAKAMA_LOG_FILE}" 2>&1 &

echo $! > "${NAKAMA_PID_FILE}"
sleep 2

if ! is_pid_running "${NAKAMA_PID_FILE}"; then
  echo "Nakama failed to start. Check ${NAKAMA_LOG_FILE}"
  exit 1
fi

echo "Nakama started."
echo "Console: http://127.0.0.1:${NAKAMA_CONSOLE_PORT}"
