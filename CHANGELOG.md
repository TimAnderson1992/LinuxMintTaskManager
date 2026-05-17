# Changelog

## 1.0.0

Initial packaged version.

- Added Processes, Performance, Startup, and Details tabs.
- Added CPU, per-core CPU, memory, disk, network, and GPU metric views.
- Added fixed-size metric history buffers to reduce memory growth.
- Added virtualized process list rendering and reduced refresh allocations.
- Added Linux Mint Cinnamon `Ctrl+Shift+Esc` integration through the `.deb` package.
- Added self-contained Debian packaging under `/opt/linux-mint-system-monitor/`.
- Added launcher, hicolor icon, `/usr/bin/linux-mint-system-monitor` wrapper, and apt/dpkg uninstall support.
- Added hardware fallback behavior for missing GPU metrics, sensors, sysfs entries, and helper commands.
