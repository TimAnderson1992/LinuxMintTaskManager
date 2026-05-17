# Debian Packaging

The package is built by:

```bash
./package-deb.sh
```

The default output is:

```text
artifacts/packages/linux-mint-system-monitor_1.0.0_amd64.deb
```

The script publishes the Avalonia app as a self-contained Linux x64 Release build, stages the package tree, and runs `dpkg-deb`.

## Installed Layout

```text
/opt/linux-mint-system-monitor/                         application files
/usr/bin/linux-mint-system-monitor                      executable wrapper
/usr/share/applications/linux-mint-system-monitor.desktop
/usr/share/icons/hicolor/256x256/apps/linux-mint-system-monitor.png
/usr/lib/linux-mint-system-monitor/cinnamon-shortcut    Cinnamon shortcut helper
```

## Maintainer Scripts

The package includes:

- `postinst`: refreshes desktop/icon caches and tries to configure the Cinnamon shortcut.
- `prerm`: removes the package-owned Cinnamon shortcut before package files are removed.
- `postrm`: refreshes desktop/icon caches after removal.

The package intentionally does not include generated `.deb` files or publish output in git. Those stay under `artifacts/`, which is ignored.

## Versioning

Set a package version with:

```bash
VERSION=1.0.1 ./package-deb.sh
```

The package file name follows:

```text
linux-mint-system-monitor_<version>_<architecture>.deb
```
