#!/usr/bin/env bash

set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "Please run with sudo: sudo bash scripts/setup-wsl2-dev.sh"
  exit 1
fi

if [[ ! -f /etc/os-release ]]; then
  echo "Unsupported Linux distribution."
  exit 1
fi

. /etc/os-release

if [[ "${ID:-}" != "ubuntu" ]]; then
  echo "This script currently supports Ubuntu only."
  exit 1
fi

echo "Updating apt sources..."
apt-get update
apt-get install -y wget gpg apt-transport-https ca-certificates software-properties-common

echo "Installing Microsoft package feed..."
wget https://packages.microsoft.com/config/ubuntu/"${VERSION_ID}"/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
dpkg -i /tmp/packages-microsoft-prod.deb
rm -f /tmp/packages-microsoft-prod.deb

echo "Installing PowerShell 7..."
apt-get update
apt-get install -y powershell

echo "Installing .NET SDK 10.0.100..."
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
mkdir -p /usr/share/dotnet
/tmp/dotnet-install.sh --version 10.0.100 --install-dir /usr/share/dotnet
ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
rm -f /tmp/dotnet-install.sh

echo
echo "Installed versions:"
dotnet --version
pwsh --version
git --version || true

echo
echo "Next steps:"
echo "1. Enable Docker Desktop WSL integration for this Ubuntu distro."
echo "2. Move the repository from /mnt/e/... to ~/workspace/..."
echo "3. Run: dotnet restore && dotnet build && dotnet test"
