# Development

Common commands:

```bash
dotnet build
dotnet run --project LinuxMintSystemMonitor.csproj
dotnet publish -c Release
./package-deb.sh
sudo apt install --reinstall ./artifacts/packages/linux-mint-system-monitor_1.0.0_amd64.deb
```

## Notes

- `bin/`, `obj/`, and `artifacts/` are generated and ignored by git.
- `package-deb.sh` does a self-contained Release publish for Linux x64 by default.
- The package build may need network access the first time it restores runtime packs.
- Linux Mint Cinnamon is the main development target.
