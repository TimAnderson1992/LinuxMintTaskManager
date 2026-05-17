#!/usr/bin/env bash
set -euo pipefail

APP_ID="linux-mint-system-monitor"
APP_NAME="Linux Mint System Monitor"
EXECUTABLE_NAME="LinuxMintSystemMonitor"
if [[ -z "${VERSION:-}" ]]; then
    if [[ "${GITHUB_REF_TYPE:-}" == "tag" && "${GITHUB_REF_NAME:-}" =~ ^v(.+)$ ]]; then
        VERSION="${BASH_REMATCH[1]}"
    elif git -C "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)" describe --tags --exact-match >/dev/null 2>&1; then
        VERSION="$(git -C "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)" describe --tags --exact-match | sed 's/^v//')"
    elif [[ -f "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/VERSION" ]]; then
        VERSION="$(tr -d '[:space:]' < "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/VERSION")"
    else
        VERSION="1.0.0"
    fi
fi
ARCHITECTURE="${ARCHITECTURE:-amd64}"
ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="${ROOT_DIR}/artifacts/deb-build"
PUBLISH_DIR="${BUILD_DIR}/publish"
PACKAGE_ROOT="${BUILD_DIR}/package"
OUTPUT_DIR="${ROOT_DIR}/artifacts/packages"
PACKAGE_FILE="${OUTPUT_DIR}/${APP_ID}_${VERSION}_${ARCHITECTURE}.deb"

case "${ARCHITECTURE}" in
    amd64)
        RUNTIME_ID="linux-x64"
        ;;
    arm64)
        RUNTIME_ID="linux-arm64"
        ;;
    armhf)
        RUNTIME_ID="linux-arm"
        ;;
    *)
        echo "Unsupported ARCHITECTURE='${ARCHITECTURE}'. Use amd64, arm64, or armhf." >&2
        exit 1
        ;;
esac

if [[ ! "${VERSION}" =~ ^[0-9][0-9A-Za-z.+:~_-]*$ ]]; then
    echo "Invalid Debian version '${VERSION}'." >&2
    exit 1
fi

if [[ ! -f "${ROOT_DIR}/assets/${APP_ID}.png" ]]; then
    echo "Missing icon: ${ROOT_DIR}/assets/${APP_ID}.png" >&2
    exit 1
fi

rm -rf "${BUILD_DIR}"
mkdir -p \
    "${PUBLISH_DIR}" \
    "${PACKAGE_ROOT}/DEBIAN" \
    "${PACKAGE_ROOT}/opt/${APP_ID}" \
    "${PACKAGE_ROOT}/usr/lib/${APP_ID}" \
    "${PACKAGE_ROOT}/usr/bin" \
    "${PACKAGE_ROOT}/usr/share/applications" \
    "${PACKAGE_ROOT}/usr/share/icons/hicolor/256x256/apps" \
    "${OUTPUT_DIR}"

dotnet publish "${ROOT_DIR}/LinuxMintSystemMonitor.csproj" \
    -c Release \
    -r "${RUNTIME_ID}" \
    --self-contained true \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    -p:PublishSingleFile=false \
    -p:PublishTrimmed=false \
    -o "${PUBLISH_DIR}"

find "${PUBLISH_DIR}" -type f -name '*.pdb' -delete
rm -f \
    "${PUBLISH_DIR}/createdump" \
    "${PUBLISH_DIR}/libmscordaccore.so" \
    "${PUBLISH_DIR}/libmscordbi.so"
cp -a "${PUBLISH_DIR}/." "${PACKAGE_ROOT}/opt/${APP_ID}/"
find "${PACKAGE_ROOT}/opt/${APP_ID}" -type f -exec chmod 0644 {} +
find "${PACKAGE_ROOT}/opt/${APP_ID}" -type f -name '*.so' -exec chmod 0755 {} +
chmod 0755 "${PACKAGE_ROOT}/opt/${APP_ID}/${EXECUTABLE_NAME}"

cat > "${PACKAGE_ROOT}/usr/bin/${APP_ID}" <<WRAPPER
#!/usr/bin/env sh
exec /opt/${APP_ID}/${EXECUTABLE_NAME} "\$@"
WRAPPER
chmod 0755 "${PACKAGE_ROOT}/usr/bin/${APP_ID}"

cat > "${PACKAGE_ROOT}/usr/lib/${APP_ID}/cinnamon-shortcut" <<'SHORTCUT'
#!/usr/bin/env sh
set -eu

APP_ID="linux-mint-system-monitor"
APP_NAME="Linux Mint System Monitor"
APP_COMMAND="/usr/bin/linux-mint-system-monitor"
APP_BINDING="<Primary><Shift>Escape"
LIST_SCHEMA="org.cinnamon.desktop.keybindings"
CUSTOM_SCHEMA="org.cinnamon.desktop.keybindings.custom-keybinding"
CUSTOM_BASE="/org/cinnamon/desktop/keybindings/custom-keybindings"
MARKER_DIR=".config/linux-mint-system-monitor"
MARKER_FILE="cinnamon-shortcut"

log() {
    echo "linux-mint-system-monitor: $*"
}

user_uid() {
    id -u "$1" 2>/dev/null || true
}

user_home() {
    getent passwd "$1" | cut -d: -f6
}

run_for_user() {
    user="$1"
    shift
    uid="$(user_uid "$user")"
    home="$(user_home "$user")"
    if [ -z "$uid" ] || [ -z "$home" ] || [ ! -S "/run/user/$uid/bus" ]; then
        return 1
    fi

    runuser -u "$user" -- env \
        HOME="$home" \
        XDG_RUNTIME_DIR="/run/user/$uid" \
        DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/$uid/bus" \
        "$@"
}

candidate_users() {
    {
        if [ "${SUDO_USER:-}" ] && [ "${SUDO_USER:-}" != "root" ]; then
            printf '%s\n' "$SUDO_USER"
        fi

        if [ "${PKEXEC_UID:-}" ]; then
            getent passwd "$PKEXEC_UID" | cut -d: -f1 || true
        fi

        if command -v loginctl >/dev/null 2>&1; then
            loginctl list-sessions --no-legend 2>/dev/null | while read -r session _rest; do
                [ -n "$session" ] || continue
                loginctl show-session "$session" -p Name --value 2>/dev/null || true
            done
        fi

        for bus in /run/user/*/bus; do
            [ -S "$bus" ] || continue
            uid="$(printf '%s\n' "$bus" | awk -F/ '{print $4}')"
            getent passwd "$uid" | cut -d: -f1 || true
        done
    } | awk 'NF && !seen[$0]++'
}

is_cinnamon_user() {
    user="$1"
    uid="$(user_uid "$user")"
    [ -n "$uid" ] || return 1

    if [ "${SUDO_USER:-}" = "$user" ] || [ "${USER:-}" = "$user" ]; then
        case "${XDG_CURRENT_DESKTOP:-}:${DESKTOP_SESSION:-}" in
            *Cinnamon*|*cinnamon*) return 0 ;;
        esac
    fi

    if command -v loginctl >/dev/null 2>&1; then
        sessions="$(loginctl list-sessions --no-legend 2>/dev/null | awk -v uid="$uid" '$3 == uid {print $1}' || true)"
        for session in $sessions; do
            desktop="$(loginctl show-session "$session" -p Desktop --value 2>/dev/null || true)"
            type="$(loginctl show-session "$session" -p Type --value 2>/dev/null || true)"
            active="$(loginctl show-session "$session" -p Active --value 2>/dev/null || true)"
            case "$active:$desktop:$type" in
                yes:*cinnamon*:x11|yes:*cinnamon*:wayland|yes:*Cinnamon*:x11|yes:*Cinnamon*:wayland)
                    return 0
                    ;;
            esac
        done
    fi

    pgrep -u "$uid" -x cinnamon >/dev/null 2>&1 || pgrep -u "$uid" -x cinnamon-session >/dev/null 2>&1
}

schema_available() {
    user="$1"
    run_for_user "$user" gsettings list-schemas 2>/dev/null | grep -qx "$LIST_SCHEMA" \
        && run_for_user "$user" gsettings list-relocatable-schemas 2>/dev/null | grep -qx "$CUSTOM_SCHEMA"
}

cinnamon_version() {
    user="$1"
    run_for_user "$user" sh -c 'cinnamon --version 2>/dev/null || cinnamon-session --version 2>/dev/null || true' \
        | head -n 1
}

session_type() {
    user="$1"
    uid="$(user_uid "$user")"

    if command -v loginctl >/dev/null 2>&1 && [ -n "$uid" ]; then
        sessions="$(loginctl list-sessions --no-legend 2>/dev/null | awk -v uid="$uid" '$3 == uid {print $1}' || true)"
        for session in $sessions; do
            active="$(loginctl show-session "$session" -p Active --value 2>/dev/null || true)"
            [ "$active" = "yes" ] || continue
            type="$(loginctl show-session "$session" -p Type --value 2>/dev/null || true)"
            desktop="$(loginctl show-session "$session" -p Desktop --value 2>/dev/null || true)"
            printf '%s/%s\n' "${desktop:-unknown}" "${type:-unknown}"
            return 0
        done
    fi

    if [ -n "$uid" ]; then
        for pid in $(pgrep -u "$uid" -x cinnamon-session 2>/dev/null || true) $(pgrep -u "$uid" -x cinnamon 2>/dev/null || true); do
            [ -r "/proc/$pid/environ" ] || continue
            desktop="$(tr '\0' '\n' < "/proc/$pid/environ" | awk -F= '$1 == "XDG_CURRENT_DESKTOP" {print $2; exit}')"
            type="$(tr '\0' '\n' < "/proc/$pid/environ" | awk -F= '$1 == "XDG_SESSION_TYPE" {print $2; exit}')"
            if [ -n "$desktop$type" ]; then
                printf '%s/%s\n' "${desktop:-cinnamon}" "${type:-unknown}"
                return 0
            fi
        done
    fi

    if [ "${SUDO_USER:-}" = "$user" ] || [ "${USER:-}" = "$user" ]; then
        printf '%s/%s\n' "${XDG_CURRENT_DESKTOP:-unknown}" "${XDG_SESSION_TYPE:-unknown}"
        return 0
    fi

    printf 'unknown/unknown\n'
}

custom_path() {
    printf '%s/%s/\n' "$CUSTOM_BASE" "$1"
}

custom_get() {
    user="$1"
    id="$2"
    key="$3"
    run_for_user "$user" gsettings get "$CUSTOM_SCHEMA:$(custom_path "$id")" "$key" 2>/dev/null || true
}

custom_set() {
    user="$1"
    id="$2"
    key="$3"
    value="$4"
    run_for_user "$user" gsettings set "$CUSTOM_SCHEMA:$(custom_path "$id")" "$key" "$value" >/dev/null 2>&1 || true
}

custom_ids() {
    user="$1"
    run_for_user "$user" gsettings get "$LIST_SCHEMA" custom-list 2>/dev/null \
        | tr -d "[]'," \
        | tr ' ' '\n' \
        | awk 'NF'
}

custom_list_value() {
    user="$1"
    run_for_user "$user" gsettings get "$LIST_SCHEMA" custom-list 2>/dev/null || printf '[]\n'
}

contains_id() {
    needle="$1"
    shift
    for value in "$@"; do
        [ "$value" = "$needle" ] && return 0
    done
    return 1
}

set_custom_ids() {
    user="$1"
    shift
    value="["
    sep=""
    for id in "$@"; do
        [ -n "$id" ] || continue
        value="$value$sep'$id'"
        sep=", "
    done
    value="$value]"
    run_for_user "$user" gsettings set "$LIST_SCHEMA" custom-list "$value" >/dev/null 2>&1 || true
}

rewrite_custom_list() {
    user="$1"
    value="$(custom_list_value "$user")"
    run_for_user "$user" gsettings set "$LIST_SCHEMA" custom-list "$value" >/dev/null 2>&1 || true
}

binding_matches() {
    printf '%s' "$1" | grep -Fq "$APP_BINDING"
}

command_is_ours() {
    command="$1"
    case "$command" in
        *"$APP_COMMAND"*|*linux-mint-system-monitor*) return 0 ;;
        *) return 1 ;;
    esac
}

marker_path() {
    home="$(user_home "$1")"
    printf '%s/%s/%s\n' "$home" "$MARKER_DIR" "$MARKER_FILE"
}

write_marker() {
    user="$1"
    id="$2"
    home="$(user_home "$user")"
    uid="$(user_uid "$user")"
    gid="$(id -g "$user")"
    mkdir -p "$home/$MARKER_DIR"
    printf '%s\n' "$id" > "$home/$MARKER_DIR/$MARKER_FILE"
    chown -R "$uid:$gid" "$home/$MARKER_DIR" >/dev/null 2>&1 || true
}

remove_marker() {
    rm -f "$(marker_path "$1")" 2>/dev/null || true
}

manual_shortcut_message() {
    user="$1"
    log "Ctrl+Shift+Esc could not be verified for $user."
    log "If the shortcut does not work, assign it manually:"
    log "Menu -> Keyboard -> Shortcuts -> Custom Shortcuts"
    log "Command: $APP_COMMAND"
    log "Shortcut: Ctrl+Shift+Esc"
}

configure_entry() {
    user="$1"
    id="$2"
    log "Cinnamon diagnostics for $user: version='$(cinnamon_version "$user")' session='$(session_type "$user")'"

    # Cinnamon notices custom entries most reliably when the entry is already
    # present in custom-list before the binding array is written.
    custom_set "$user" "$id" name "$APP_NAME"
    custom_set "$user" "$id" command "$APP_COMMAND"
    rewrite_custom_list "$user"
    sleep 0.2

    # Some Cinnamon versions do not apply a custom binding on the first dconf
    # notification during package install. Write the binding twice, after the
    # custom-list update, then nudge custom-list again to refresh the cache.
    custom_set "$user" "$id" binding "['$APP_BINDING']"
    sleep 0.2
    custom_set "$user" "$id" binding "['$APP_BINDING']"
    rewrite_custom_list "$user"
    sleep 0.2

    final_binding="$(custom_get "$user" "$id" binding)"
    if ! binding_matches "$final_binding" && run_for_user "$user" sh -c 'command -v dconf >/dev/null 2>&1'; then
        run_for_user "$user" dconf write "$(custom_path "$id")binding" "['$APP_BINDING']" >/dev/null 2>&1 || true
        rewrite_custom_list "$user"
        sleep 0.2
        final_binding="$(custom_get "$user" "$id" binding)"
    fi

    log "Cinnamon shortcut final binding for $user/$id: ${final_binding:-unavailable}"
    if binding_matches "$final_binding"; then
        log "Cinnamon shortcut binding verification succeeded for $user."
    else
        log "Cinnamon shortcut binding verification failed for $user."
        manual_shortcut_message "$user"
    fi

    write_marker "$user" "$id"
}

install_shortcut_for_user() {
    user="$1"
    if ! is_cinnamon_user "$user"; then
        log "skipping Ctrl+Shift+Esc setup for $user: Cinnamon session not detected."
        return 0
    fi

    if ! schema_available "$user"; then
        log "skipping Ctrl+Shift+Esc setup for $user: Cinnamon keybinding schemas unavailable."
        return 0
    fi

    ids="$(custom_ids "$user" | tr '\n' ' ')"
    conflict=""
    reusable=""

    for id in $ids; do
        [ "$id" = "__dummy__" ] && continue
        name="$(custom_get "$user" "$id" name)"
        command="$(custom_get "$user" "$id" command)"
        binding="$(custom_get "$user" "$id" binding)"

        if command_is_ours "$command" || printf '%s' "$name" | grep -Fq "$APP_NAME"; then
            reusable="$id"
            if binding_matches "$binding" && command_is_ours "$command"; then
                configure_entry "$user" "$id"
                log "Ctrl+Shift+Esc shortcut already configured for $user."
                return 0
            fi
            break
        fi

        if binding_matches "$binding"; then
            conflict="$id"
        fi
    done

    if [ -n "$reusable" ]; then
        configure_entry "$user" "$reusable"
        log "updated Cinnamon Ctrl+Shift+Esc shortcut for $user."
        return 0
    fi

    if [ -n "$conflict" ]; then
        log "Ctrl+Shift+Esc is already assigned for $user; leaving existing shortcut '$conflict' unchanged."
        log "Set it manually to $APP_COMMAND if you want Linux Mint System Monitor on Ctrl+Shift+Esc."
        return 0
    fi

    next=""
    n=0
    while [ "$n" -lt 100 ]; do
        candidate="custom$n"
        found=0
        for id in $ids; do
            [ "$id" = "$candidate" ] && found=1
        done
        if [ "$found" -eq 0 ]; then
            next="$candidate"
            break
        fi
        n=$((n + 1))
    done

    if [ -z "$next" ]; then
        log "could not find an available Cinnamon custom shortcut slot for $user."
        return 0
    fi

    set -- $ids
    if contains_id "__dummy__" "$@"; then
        new_ids=""
        for id in "$@"; do
            [ "$id" = "__dummy__" ] && continue
            new_ids="$new_ids $id"
        done
        # shellcheck disable=SC2086
        set_custom_ids "$user" $new_ids "$next" "__dummy__"
    else
        # shellcheck disable=SC2086
        set_custom_ids "$user" $ids "$next"
    fi

    configure_entry "$user" "$next"
    log "created Cinnamon Ctrl+Shift+Esc shortcut for $user."
}

remove_shortcut_for_user() {
    user="$1"
    marker="$(marker_path "$user")"
    [ -f "$marker" ] || return 0
    id="$(cat "$marker" 2>/dev/null || true)"
    [ -n "$id" ] || return 0

    if schema_available "$user"; then
        command="$(custom_get "$user" "$id" command)"
        name="$(custom_get "$user" "$id" name)"
        if command_is_ours "$command" || printf '%s' "$name" | grep -Fq "$APP_NAME"; then
            ids="$(custom_ids "$user" | tr '\n' ' ')"
            kept=""
            for current in $ids; do
                [ "$current" = "$id" ] && continue
                kept="$kept $current"
            done
            # shellcheck disable=SC2086
            set_custom_ids "$user" $kept
            custom_set "$user" "$id" name ""
            custom_set "$user" "$id" command ""
            custom_set "$user" "$id" binding "[]"
            log "removed Cinnamon Ctrl+Shift+Esc shortcut for $user."
        fi
    fi

    remove_marker "$user"
}

action="${1:-install}"
case "$action" in
    install)
        for user in $(candidate_users); do
            install_shortcut_for_user "$user" || true
        done
        ;;
    remove)
        for user in $(candidate_users); do
            remove_shortcut_for_user "$user" || true
        done
        ;;
    *)
        echo "Usage: $0 install|remove" >&2
        exit 2
        ;;
esac

exit 0
SHORTCUT
chmod 0755 "${PACKAGE_ROOT}/usr/lib/${APP_ID}/cinnamon-shortcut"

cat > "${PACKAGE_ROOT}/usr/share/applications/${APP_ID}.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=${APP_NAME}
GenericName=System Monitor
Comment=Windows Task Manager-style system monitor for Linux
Exec=/usr/bin/${APP_ID}
Icon=${APP_ID}
Terminal=false
StartupNotify=true
Categories=System;Monitor;
Keywords=Task Manager;System Monitor;Processes;Performance;CPU;Memory;
DESKTOP
chmod 0644 "${PACKAGE_ROOT}/usr/share/applications/${APP_ID}.desktop"

install -m 0644 \
    "${ROOT_DIR}/assets/${APP_ID}.png" \
    "${PACKAGE_ROOT}/usr/share/icons/hicolor/256x256/apps/${APP_ID}.png"

INSTALLED_SIZE="$(du -ks "${PACKAGE_ROOT}" | awk '{print $1}')"
cat > "${PACKAGE_ROOT}/DEBIAN/control" <<CONTROL
Package: ${APP_ID}
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: ${ARCHITECTURE}
Installed-Size: ${INSTALLED_SIZE}
Maintainer: Linux Mint System Monitor Maintainers <maintainers@example.invalid>
Depends: libc6, libfontconfig1, libfreetype6, libx11-6, libx11-xcb1, libxcb1, libxrandr2, libxi6, libxcursor1, libxinerama1, libglib2.0-0
Description: Windows Task Manager-style system monitor for Linux
 Linux Mint System Monitor provides process, performance, startup,
 hardware, and system detail views for Linux desktops.
CONTROL

cat > "${PACKAGE_ROOT}/DEBIAN/postinst" <<'POSTINST'
#!/usr/bin/env sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database /usr/share/applications >/dev/null 2>&1 || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor >/dev/null 2>&1 || true
fi
if [ -x /usr/lib/linux-mint-system-monitor/cinnamon-shortcut ]; then
    /usr/lib/linux-mint-system-monitor/cinnamon-shortcut install || true
fi
exit 0
POSTINST

cat > "${PACKAGE_ROOT}/DEBIAN/prerm" <<'PRERM'
#!/usr/bin/env sh
set -e
case "$1" in
    remove|purge|deconfigure)
        if [ -x /usr/lib/linux-mint-system-monitor/cinnamon-shortcut ]; then
            /usr/lib/linux-mint-system-monitor/cinnamon-shortcut remove || true
        fi
        ;;
esac
exit 0
PRERM

cat > "${PACKAGE_ROOT}/DEBIAN/postrm" <<'POSTRM'
#!/usr/bin/env sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database /usr/share/applications >/dev/null 2>&1 || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor >/dev/null 2>&1 || true
fi
exit 0
POSTRM

chmod 0755 "${PACKAGE_ROOT}/DEBIAN/postinst" "${PACKAGE_ROOT}/DEBIAN/prerm" "${PACKAGE_ROOT}/DEBIAN/postrm"

find "${PACKAGE_ROOT}" -type d -exec chmod 0755 {} +
dpkg-deb --build --root-owner-group "${PACKAGE_ROOT}" "${PACKAGE_FILE}"

echo "Built package: ${PACKAGE_FILE}"
