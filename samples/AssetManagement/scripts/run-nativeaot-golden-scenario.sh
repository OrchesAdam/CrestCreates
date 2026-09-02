#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST_PROJECT="$SCRIPT_DIR/../src/CrestCreates.Sample.AssetManagement.Host"
PUBLISH_DIR=$(mktemp -d)
trap 'rm -rf "$PUBLISH_DIR"' EXIT

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
