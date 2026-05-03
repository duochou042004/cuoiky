#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="${ROOT_DIR}/docker-compose.yml"

cd "${ROOT_DIR}"

if ! command -v docker >/dev/null 2>&1; then
  echo "Docker is not installed or not available in PATH." >&2
  exit 1
fi

if ! docker compose version >/dev/null 2>&1; then
  echo "Docker Compose plugin is not available." >&2
  exit 1
fi

echo "Starting Smart Parking Docker dependencies"
echo "Project root: ${ROOT_DIR}"

if [[ "${SMARTPARKING_REBUILD:-false}" == "true" ]]; then
  docker compose -f "${COMPOSE_FILE}" up -d --build --remove-orphans
else
  docker compose -f "${COMPOSE_FILE}" up -d --remove-orphans
fi

echo
echo "Service status:"
docker compose -f "${COMPOSE_FILE}" ps

echo
echo "Useful URLs:"
echo "- MongoDB: localhost:27017"
echo "- Backend expected MongoDB connection: mongodb://localhost:27017"
echo "- AI services are intentionally not started on this branch because camera work is deferred."
