#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="${HOME}/.local/share/linux-mint-system-monitor"
DESKTOP_DIR="${HOME}/.local/share/applications"
DESKTOP_FILE="${DESKTOP_DIR}/linux-mint-system-monitor.desktop"

cd "${SCRIPT_DIR}"

echo "Publishing Linux Mint System Monitor..."
dotnet publish -c Release -o "${APP_DIR}"

mkdir -p "${DESKTOP_DIR}"

cat > "${DESKTOP_FILE}" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Linux Mint System Monitor
GenericName=System Monitor
Comment=Windows Task Manager-style system monitor for Linux
Exec=${APP_DIR}/LinuxMintSystemMonitor
Icon=linux-mint-system-monitor
Terminal=false
StartupNotify=true
Categories=System;Monitor;
Keywords=Task Manager;System Monitor;Processes;Performance;CPU;Memory;
DESKTOP

chmod +x "${DESKTOP_FILE}"

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "${DESKTOP_DIR}" >/dev/null 2>&1 || true
fi

echo
echo "Installed Task Manager to:"
echo "  ${APP_DIR}"
echo "Desktop launcher:"
echo "  ${DESKTOP_FILE}"
echo
echo "To bind Ctrl+Shift+Esc in Linux Mint:"
echo "  1. Open System Settings > Keyboard > Shortcuts."
echo "  2. Add a custom shortcut named Linux Mint System Monitor."
echo "  3. Set the command to: ${APP_DIR}/LinuxMintSystemMonitor"
echo "  4. Assign Ctrl+Shift+Esc."
