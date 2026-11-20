#!/usr/bin/env bash
# Build the ErpManagement sample app image (frontend + backend + dev TLS cert).
#
# Environment note: Docker Hub is unreachable in the target environment, so node/sdk
# images cannot be pulled for in-container builds. We therefore pre-build on the host
# (npm + dotnet publish) and package onto mcr.microsoft.com/dotnet/aspnet:10.0, which is
# reachable. See deploy/README.md.
set -euo pipefail

cd "$(dirname "$0")/.."
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
DOTNET="$DOTNET_ROOT/dotnet"
RUNTIME="${CONTAINER_RUNTIME:-podman}"

echo "[1/4] Build frontend (Vite -> ErpManagement.Web/wwwroot)"
(cd erp-web && npm ci --no-audit --no-fund && npm run build)

echo "[2/4] Publish backend (framework-dependent, portable, no trim)"
"$DOTNET" publish ErpManagement.Web/ErpManagement.Web.csproj \
  -c Release --self-contained false \
  -p:PublishTrimmed=false -p:PublishAot=false \
  -o deploy/publish -m:1 -p:UseSharedCompilation=false

echo "[3/4] Generate localhost development TLS certificate"
"$DOTNET" dev-certs https -ep deploy/erp-dev.pfx -p erp-dev-cert

echo "[4/4] Build app image with ${RUNTIME}"
(cd deploy && "$RUNTIME" build -t "${ERP_APP_IMAGE:-erp-management:latest}" .)

echo "Done. Set the deployment credentials described in deploy/README.md, then run Compose from deploy/."