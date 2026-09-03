#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST_PROJECT="$SCRIPT_DIR/../src/CrestCreates.Sample.AssetManagement.Host"
PUBLISH_DIR=$(mktemp -d)
CONTAINER_NAME="crestcreates-asset-pg-${$}"
CONTAINER_STARTED=0
cleanup() {
  rm -rf "$PUBLISH_DIR"
  if [[ "$CONTAINER_STARTED" == 1 ]]; then
    docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

if [[ -z "${ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING:-}" ]]; then
  docker run --rm -d --name "$CONTAINER_NAME" \
    -e POSTGRES_DB=crest_asset_management \
    -e POSTGRES_USER=crest \
    -e POSTGRES_PASSWORD=crest \
    -p 127.0.0.1::5432 postgres:16-alpine >/dev/null
  CONTAINER_STARTED=1
  READY=0
  for _ in {1..60}; do
    if docker exec "$CONTAINER_NAME" pg_isready -U crest -d crest_asset_management >/dev/null 2>&1; then
      READY=1
      break
    fi
    sleep 1
  done
  if [[ "$READY" != 1 ]]; then
    echo "PostgreSQL container did not become ready in time." >&2
    exit 1
  fi
  PORT="$(docker port "$CONTAINER_NAME" 5432/tcp | sed -E 's/.*:([0-9]+)$/\1/')"
  export ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING="Host=127.0.0.1;Port=$PORT;Database=crest_asset_management;Username=crest;Password=crest"
fi
export ASSET_MANAGEMENT_RUNTIME_SCHEMA="crest_asset_runtime_aot_${$}"

dotnet publish "$HOST_PROJECT" \
  --disable-build-servers \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:CrestCreatesPublishMode=aot \
  -o "$PUBLISH_DIR"

SCENARIO_OUTPUT=$("$PUBLISH_DIR/CrestCreates.Sample.AssetManagement.Host" --golden-scenario)
printf '%s\n' "$SCENARIO_OUTPUT"
grep -Fq "CRESTCREATES_ASSET_MANAGEMENT_GOLDEN_OK" <<<"$SCENARIO_OUTPUT"
