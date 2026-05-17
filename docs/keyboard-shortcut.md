# Cinnamon Ctrl+Shift+Esc Shortcut

The `.deb` package tries to configure `Ctrl+Shift+Esc` automatically on Linux Mint Cinnamon.

The shortcut launches:

```bash
/usr/bin/linux-mint-system-monitor
```

## GSettings / dconf Paths

Cinnamon stores the custom shortcut list here:

```text
schema: org.cinnamon.desktop.keybindings
key:    custom-list
```

Each custom shortcut entry uses the relocatable schema:

```text
schema: org.cinnamon.desktop.keybindings.custom-keybinding
path:   /org/cinnamon/desktop/keybindings/custom-keybindings/<custom-id>/
keys:   name, command, binding
```

For this app, the package sets:

```text
name:    Linux Mint System Monitor
command: /usr/bin/linux-mint-system-monitor
binding: ['<Primary><Shift>Escape']
```

## Duplicate and Conflict Handling

The helper reuses an existing Linux Mint System Monitor shortcut entry when it finds one.

If `Ctrl+Shift+Esc` is already assigned to an unrelated command, the helper leaves that shortcut alone and prints a message. It does not steal the user's existing shortcut.

The package writes a small marker file:

```text
~/.config/linux-mint-system-monitor/cinnamon-shortcut
```

That marker records which custom shortcut entry was created or adopted by the package. During uninstall, the package removes only that marker-owned entry and preserves other custom shortcuts.

## Unsupported Desktops

The helper only acts when it detects a Cinnamon user session with a user DBus session bus. On other desktops, headless installs, or systems without the Cinnamon schemas, it exits successfully without changing shortcuts.
