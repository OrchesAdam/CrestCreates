#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST_PROJECT="$SCRIPT_DIR/../src/CrestCreates.Sample.AssetManagement.Host"
PROFILE="${1:-all}"
ARTIFACT_DIR="$SCRIPT_DIR/../../../artifacts/asset-nativeaot"
mkdir -p "$ARTIFACT_DIR"
ARTIFACT_DIR="$(cd "$ARTIFACT_DIR" && pwd)"
RUN_DIR=$(mktemp -d "$ARTIFACT_DIR/run-XXXXXX")
PUBLISH_DIR="$RUN_DIR/publish"
mkdir -p "$PUBLISH_DIR"
CONTAINER_NAME="crestcreates-asset-pg-${$}"
CONTAINER_STARTED=0
cleanup() {
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
dotnet publish "$HOST_PROJECT" \
  --disable-build-servers \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:CrestCreatesPublishMode=aot \
  -o "$PUBLISH_DIR" \
  2>&1 | tee "$RUN_DIR/publish.log"

run_v1() {
  local output
  export AssetManagement__DatabasePath="$RUN_DIR/v1.db"
  export ASSET_MANAGEMENT_RUNTIME_SCHEMA="crest_asset_runtime_aot_v1_${$}"
  if ! output=$("$PUBLISH_DIR/CrestCreates.Sample.AssetManagement.Host" --golden-scenario 2>&1); then
    printf '%s\n' "$output" | tee "$RUN_DIR/v1-run.log"
    return 1
  fi
  printf '%s\n' "$output" | tee "$RUN_DIR/v1-run.log"
  grep -Fq "CRESTCREATES_ASSET_MANAGEMENT_GOLDEN_OK" <<<"$output"
}

run_candidate() {
  local output
  export AssetManagement__DatabasePath="$RUN_DIR/candidate-v2.db"
  export ASSET_MANAGEMENT_RUNTIME_SCHEMA="crest_asset_runtime_aot_candidate_v2_${$}"
  if ! output=$("$PUBLISH_DIR/CrestCreates.Sample.AssetManagement.Host" --golden-scenario --golden-scenario-profile=asset-v2-candidate 2>&1); then
    printf '%s\n' "$output" | tee "$RUN_DIR/candidate-v2-run.log"
    return 1
  fi
  printf '%s\n' "$output" | tee "$RUN_DIR/candidate-v2-run.log"
  grep -Fq "CRESTCREATES_ASSET_MANAGEMENT_CANDIDATE_V2_GOLDEN_OK" <<<"$output"
}

check_invalid_selector() {
  export AssetManagement__DatabasePath="$RUN_DIR/invalid-selector.db"
  export ASSET_MANAGEMENT_RUNTIME_SCHEMA="crest_asset_runtime_aot_invalid_${$}"
  if timeout 10s "$PUBLISH_DIR/CrestCreates.Sample.AssetManagement.Host" --golden-scenario-profile=asset-v2-candidate >"$RUN_DIR/candidate-v2-invalid-selector.log" 2>&1; then
    echo "Candidate profile unexpectedly started without --golden-scenario." >&2
    return 1
  fi
  grep -Fq "only valid with --golden-scenario" "$RUN_DIR/candidate-v2-invalid-selector.log"
}

case "$PROFILE" in
  all)
    run_v1
    run_candidate
    check_invalid_selector
    ;;
  v1)
    run_v1
    ;;
  candidate-v2)
    run_candidate
    check_invalid_selector
    ;;
  *)
    echo "Unknown NativeAOT golden scenario profile: $PROFILE" >&2
    exit 2
    ;;
esac

printf 'NativeAOT artifacts: %s\n' "$RUN_DIR"
printf 'NativeAOT binary: %s/CrestCreates.Sample.AssetManagement.Host\n' "$PUBLISH_DIR"
