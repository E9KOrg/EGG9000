#!/usr/bin/env bash
set -euo pipefail

SRC_DIR="${1:-/opt/egg9000/incoming}"
BASE_DIR="/opt/egg9000"
BLUE_DIR="${BASE_DIR}/blue"
GREEN_DIR="${BASE_DIR}/green"
BLUE_SERVICE="egg9000-blue.service"
GREEN_SERVICE="egg9000-green.service"
HEALTH_TIMEOUT_SEC="${HEALTH_TIMEOUT_SEC:-90}"
STABILIZE_SEC="${STABILIZE_SEC:-15}"

if [[ ! -d "$SRC_DIR" ]]; then
  echo "Source directory does not exist: $SRC_DIR" >&2
  exit 1
fi

if systemctl is-active --quiet "$BLUE_SERVICE"; then
  ACTIVE="blue"
  TARGET="green"
  ACTIVE_SERVICE="$BLUE_SERVICE"
  TARGET_SERVICE="$GREEN_SERVICE"
  TARGET_DIR="$GREEN_DIR"
elif systemctl is-active --quiet "$GREEN_SERVICE"; then
  ACTIVE="green"
  TARGET="blue"
  ACTIVE_SERVICE="$GREEN_SERVICE"
  TARGET_SERVICE="$BLUE_SERVICE"
  TARGET_DIR="$BLUE_DIR"
else
  ACTIVE="none"
  TARGET="blue"
  ACTIVE_SERVICE=""
  TARGET_SERVICE="$BLUE_SERVICE"
  TARGET_DIR="$BLUE_DIR"
fi

echo "Active slot: $ACTIVE"
echo "Deploy target: $TARGET ($TARGET_SERVICE)"

mkdir -p "$BLUE_DIR" "$GREEN_DIR"
rsync -a --delete --exclude '.env' --exclude '*.log' "$SRC_DIR"/ "$TARGET_DIR"/

systemctl daemon-reload
systemctl restart "$TARGET_SERVICE"

START_TIME="$(date +%s)"
while true; do
  if systemctl is-active --quiet "$TARGET_SERVICE"; then
    break
  fi

  NOW="$(date +%s)"
  ELAPSED="$(( NOW - START_TIME ))"
  if (( ELAPSED >= HEALTH_TIMEOUT_SEC )); then
    echo "New service failed to become active in ${HEALTH_TIMEOUT_SEC}s: $TARGET_SERVICE" >&2
    echo "Recent logs:"
    journalctl -u "$TARGET_SERVICE" -n 100 --no-pager || true
    exit 1
  fi
  sleep 2
done

sleep "$STABILIZE_SEC"
if ! systemctl is-active --quiet "$TARGET_SERVICE"; then
  echo "New service became unhealthy during stabilization: $TARGET_SERVICE" >&2
  journalctl -u "$TARGET_SERVICE" -n 100 --no-pager || true
  exit 1
fi

if [[ -n "$ACTIVE_SERVICE" ]]; then
  systemctl stop "$ACTIVE_SERVICE"
  echo "Stopped previous active service: $ACTIVE_SERVICE"
fi

echo "Deployment completed. Current active service: $TARGET_SERVICE"
