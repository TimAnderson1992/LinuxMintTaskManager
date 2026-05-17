#!/usr/bin/env bash
set -euo pipefail

REPO="TimAnderson1992/LinuxMintTaskManager"
API_URL="https://api.github.com/repos/${REPO}/releases/latest"
TMP_DIR="$(mktemp -d)"

cleanup() {
    rm -rf "${TMP_DIR}"
}
trap cleanup EXIT

fail() {
    echo "linux-mint-system-monitor installer: $*" >&2
    exit 1
}

download_text() {
    local url="$1"
    if command -v curl >/dev/null 2>&1; then
        curl -fsSL "$url"
        return
    fi

    if command -v wget >/dev/null 2>&1; then
        wget -qO- "$url"
        return
    fi

    fail "curl or wget is required to download the latest release."
}

download_file() {
    local url="$1"
    local output="$2"
    if command -v curl >/dev/null 2>&1; then
        curl -fL "$url" -o "$output"
        return
    fi

    if command -v wget >/dev/null 2>&1; then
        wget -O "$output" "$url"
        return
    fi

    fail "curl or wget is required to download the latest release."
}

if ! command -v sudo >/dev/null 2>&1; then
    fail "sudo is required so apt can install the package."
fi

release_json="${TMP_DIR}/latest-release.json"
download_text "$API_URL" > "$release_json"

asset_url="$(
    sed -n 's/.*"browser_download_url": "\(.*linux-mint-system-monitor_.*_amd64\.deb\)".*/\1/p' "$release_json" \
        | head -n 1
)"

if [[ -z "$asset_url" ]]; then
    fail "could not find an amd64 .deb asset on the latest GitHub release."
fi

package_name="$(basename "$asset_url")"
package_path="${TMP_DIR}/${package_name}"

echo "Downloading ${package_name}..."
download_file "$asset_url" "$package_path"

echo "Installing ${package_name}..."
sudo apt install --reinstall "$package_path"

echo
echo "Linux Mint System Monitor is installed."
echo "Launch it from the menu or run: linux-mint-system-monitor"
