# Linux Mint System Monitor

Linux Mint System Monitor is a desktop system monitor for Linux Mint. It is inspired by Windows Task Manager, but built for a Linux Mint desktop instead of trying to be a clone for every platform.

The goal is simple: press `Ctrl+Shift+Esc`, see what is running, check CPU/memory/disk/network/GPU activity, and get back to work.

## Quick Install

Most users should just download the `.deb` from GitHub Releases. You do not need the .NET SDK, and you do not need to compile anything.

1. Download the latest `.deb` from:
   <https://github.com/TimAnderson1992/LinuxMintTaskManager/releases>
2. Double-click it in Linux Mint and install it.
3. Press `Ctrl+Shift+Esc` to open Linux Mint System Monitor.

If you prefer the terminal:

```bash
sudo apt install ./linux-mint-system-monitor_1.0.0_amd64.deb
```

There is also a small installer script for the latest GitHub release:

```bash
curl -fsSL https://raw.githubusercontent.com/TimAnderson1992/LinuxMintTaskManager/main/install-from-github.sh -o /tmp/install-linux-mint-system-monitor.sh
bash /tmp/install-linux-mint-system-monitor.sh
```

With `wget`:

```bash
wget -O /tmp/install-linux-mint-system-monitor.sh https://raw.githubusercontent.com/TimAnderson1992/LinuxMintTaskManager/main/install-from-github.sh
bash /tmp/install-linux-mint-system-monitor.sh
```

Uninstall with:

```bash
sudo apt remove linux-mint-system-monitor
```

On Linux Mint Cinnamon, the `.deb` tries to create a `Ctrl+Shift+Esc` custom keyboard shortcut. If that shortcut is already assigned to something else, the installer leaves your existing shortcut alone and prints a message.

## Features

- Processes tab with grouped process views and process actions
- Performance tab for CPU, memory, disk, network, and GPU metrics
- Startup tab for desktop autostart entries
- Details tab for machine and OS information
- Per-core CPU graphs with responsive layout
- Fixed-size history buffers to avoid runaway graph memory usage
- Lightweight diagnostics logging for heap size, allocation rate, process row counts, and history buffer counts
- Hardware fallback handling for missing GPU metrics, missing sensors, restricted sysfs entries, and unavailable helper commands
- Linux Mint Cinnamon `Ctrl+Shift+Esc` shortcut integration when installed from the `.deb`
- Debian package build script

## Screenshots

Screenshots are not in the repo yet. They should be added once the main screens settle down a bit.

## Current Status

This is usable, but still actively being improved. The main workflows are in place, the app can be packaged as a `.deb`, and it has Cinnamon shortcut integration. There are still hardware and desktop combinations that need more testing.

Linux Mint Cinnamon is the primary target. Ubuntu, Debian, Fedora, Arch-based systems, and other desktops may work, but they are not the main target yet.

## Known Limitations

- GPU metrics depend on hardware, kernel, and driver support.
- Some sensors may show `Not available`, especially on laptops, VMs, SBCs, and systems with restricted sensor access.
- NVIDIA metrics usually require `nvidia-smi`.
- AMD and Intel live GPU metrics depend on what the kernel exposes through sysfs.
- Cinnamon shortcut integration is only for Linux Mint Cinnamon. Other desktops may not get automatic `Ctrl+Shift+Esc` setup.
- Wayland support may vary by distro, driver, and Avalonia backend behavior.

## Build From Source

This section is for developers and contributors. Normal users should install the `.deb` from GitHub Releases instead.

Requirements:

- .NET SDK 9
- Linux desktop session with X11, or Wayland where Avalonia support works on your setup

Common commands:

```bash
dotnet restore
dotnet build
dotnet run --project LinuxMintSystemMonitor.csproj
dotnet publish -c Release
```

## Packaging

You only need this if you want to build a local `.deb` yourself.

```bash
./package-deb.sh
```

The package is written to:

```text
artifacts/packages/linux-mint-system-monitor_1.0.0_amd64.deb
```

Install or reinstall that local build with:

```bash
sudo apt install --reinstall ./artifacts/packages/linux-mint-system-monitor_1.0.0_amd64.deb
```

The installed app lives in:

```text
/opt/linux-mint-system-monitor/
```

The launcher command is:

```bash
/usr/bin/linux-mint-system-monitor
```

For release tagging notes, see [docs/releases.md](docs/releases.md).

## Why I Made This

I like the directness of Windows Task Manager. Linux has plenty of good system tools, but on my Mint desktop I wanted one that felt familiar, opened with the same shortcut, and showed the things I check most often without making me dig around.

This project is my attempt at that. It is not trying to replace every Linux monitoring tool. It is trying to be a practical task-manager-style app for Linux Mint.

## Contributing

Issues and pull requests are welcome. The most useful contributions right now are:

- Testing on different Linux Mint versions
- Testing Cinnamon shortcut behavior
- Hardware reports for GPU/sensor fallback behavior
- Small UI fixes for different screen sizes and scaling settings
- Performance and memory improvements that keep the UI responsive

Please keep changes focused. This app is intentionally straightforward.
