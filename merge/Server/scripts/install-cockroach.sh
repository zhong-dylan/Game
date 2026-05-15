#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "${SCRIPT_DIR}/common.sh"

ensure_env
ensure_dirs

ARCH="$(uname -m)"
OS="$(uname -s)"
VERSION="${COCKROACH_VERSION:-25.1.10}"

if [[ "${OS}" != "Darwin" ]]; then
  echo "This installer currently targets macOS only."
  exit 1
fi

case "${ARCH}" in
  arm64)
    PLATFORM="darwin-11.0-arm64"
    ;;
  x86_64)
    PLATFORM="darwin-10.9-amd64"
    ;;
  *)
    echo "Unsupported architecture: ${ARCH}"
    exit 1
    ;;
esac

ARCHIVE_NAME="cockroach-v${VERSION}.${PLATFORM}.tgz"
DOWNLOAD_URL="https://binaries.cockroachdb.com/${ARCHIVE_NAME}"
EXTRACT_DIR="${SERVER_DIR}/cockroach-v${VERSION}.${PLATFORM}"
ARCHIVE_PATH="${SERVER_DIR}/${ARCHIVE_NAME}"

if [[ -x "${EXTRACT_DIR}/cockroach" ]]; then
  echo "CockroachDB already installed: ${EXTRACT_DIR}/cockroach"
  exit 0
fi

echo "Downloading ${DOWNLOAD_URL}"
curl -fL "${DOWNLOAD_URL}" -o "${ARCHIVE_PATH}"

echo "Extracting ${ARCHIVE_NAME}"
tar -xzf "${ARCHIVE_PATH}" -C "${SERVER_DIR}"
chmod +x "${EXTRACT_DIR}/cockroach"
rm -f "${ARCHIVE_PATH}"

echo "Installed CockroachDB: ${EXTRACT_DIR}/cockroach"
echo "If needed, set COCKROACH_BINARY=${EXTRACT_DIR}/cockroach in Server/.env"
