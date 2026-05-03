#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="${ROOT_DIR}/docker-compose.yml"

cd "${ROOT_DIR}"

if ! command -v docker >/dev/null 2>&1; then
  echo "Docker is not installed or not available in PATH." >&2
  exit 1
fi

echo "Stopping and removing Smart Parking Docker resources only..."
docker compose -f "${COMPOSE_FILE}" down --volumes --rmi local --remove-orphans

echo
echo "Pruning dangling Docker build cache for this machine. Use SMARTPARKING_PRUNE_ALL=true to prune all unused build cache."
if [[ "${SMARTPARKING_PRUNE_ALL:-false}" == "true" ]]; then
  docker builder prune -af
else
  docker builder prune -f
fi

echo
echo "Remaining Smart Parking containers/images/volumes:"
(docker ps -a --filter "name=smartparking" --format "table {{.Names}}\t{{.Status}}" || true)
(docker images --format "{{.Repository}}:{{.Tag}}" | grep -E '(^cuoiky-|smartparking)' || true)
(docker volume ls --format "{{.Name}}" | grep -E 'smartparking|cuoiky' || true)
