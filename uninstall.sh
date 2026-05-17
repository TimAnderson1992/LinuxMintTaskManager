#!/usr/bin/env bash
set -euo pipefail

APP_DIR="${HOME}/.local/share/linux-mint-system-monitor"
DESKTOP_FILE="${HOME}/.local/share/applications/linux-mint-system-monitor.desktop"

rm -rf "${APP_DIR}"
rm -f "${DESKTOP_FILE}"

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "${HOME}/.local/share/applications" >/dev/null 2>&1 || true
fi

echo "Removed Linux Mint System Monitor user install."
