#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST_PROJECT="$SCRIPT_DIR/../src/CrestCreates.Sample.Procurement.Host"
PUBLISH_DIR=$(mktemp -d)
trap 'rm -rf "$PUBLISH_DIR"' EXIT

echo "=== Publishing NativeAOT binary ==="
dotnet publish "$HOST_PROJECT" \
  --disable-build-servers \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:CrestCreatesPublishMode=aot \
  -o "$PUBLISH_DIR"

echo "=== Running Golden Scenario ==="
SCENARIO_OUTPUT=$("$PUBLISH_DIR/CrestCreates.Sample.Procurement.Host" --golden-scenario)
printf '%s\n' "$SCENARIO_OUTPUT"

if ! grep -Fq "CRESTCREATES_PROCUREMENT_SAMPLE_OK" <<<"$SCENARIO_OUTPUT"; then
  echo "Golden scenario did not emit the success sentinel." >&2
  exit 1
fi
