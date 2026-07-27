#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST_PROJECT="$SCRIPT_DIR/../src/CrestCreates.Sample.Procurement.Host"
PUBLISH_DIR=$(mktemp -d)

echo "=== Publishing NativeAOT binary ==="
dotnet publish "$HOST_PROJECT" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishAot=true \
  -p:CrestCreatesPublishMode=aot \
  -o "$PUBLISH_DIR"

echo "=== Running Golden Scenario ==="
"$PUBLISH_DIR/CrestCreates.Sample.Procurement.Host"

EXIT_CODE=$?
rm -rf "$PUBLISH_DIR"
exit $EXIT_CODE
