using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace LinuxMintSystemMonitor;

internal sealed record SystemDetail(string Name, string Value);

internal static class SystemDetailsReader
{
    public static IReadOnlyList<SystemDetail> Read()
    {
        var osRelease = ReadOsRelease();
        var hostInfo = ReadHostnamectl();
        return
        [
            new("Computer name / hostname", hostInfo.GetValueOrDefault("Static hostname") ?? Environment.MachineName),
            new("OS name and version", hostInfo.GetValueOrDefault("Operating System") ?? ReadOsName(osRelease)),
            new("Kernel version", ReadKernelVersion(hostInfo)),
            new("Desktop environment", hostInfo.GetValueOrDefault("Desktop") ?? ReadDesktopEnvironment()),
            new("CPU model", ReadCpuModel()),
            new("CPU cores / logical processors", ReadCpuCounts()),
            new("Total RAM", FormatBytes(ReadTotalRam())),
            new("Disk model and capacity", ReadDiskSummary()),
            new("GPU name", ReadGpuName()),
            new("Network adapters", ReadNetworkAdapters()),
            new("Battery status", ReadBatteryStatus()),
            new("Boot time / uptime", ReadBootTimeAndUptime()),
            new("Machine type", ReadMachineType(hostInfo)),
            new("BIOS/UEFI info", ReadBiosInfo())
        ];
    }

    private static Dictionary<string, string> ReadHostnamectl()
    {
        var output = RunCommand("hostnamectl", "");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2)
            {
                values[parts[0].Trim()] = parts[1].Trim();
            }
        }

        return values;
    }

    private static Dictionary<string, string> ReadOsRelease()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var line in File.ReadLines("/etc/os-release"))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    values[parts[0]] = parts[1].Trim().Trim('"');
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return values;
    }

    private static string ReadOsName(Dictionary<string, string> osRelease)
    {
        if (osRelease.TryGetValue("PRETTY_NAME", out var prettyName))
        {
            return prettyName;
        }

        var name = osRelease.GetValueOrDefault("NAME") ?? "Linux";
        var version = osRelease.GetValueOrDefault("VERSION") ?? osRelease.GetValueOrDefault("VERSION_ID");
        return string.IsNullOrWhiteSpace(version) ? name : $"{name} {version}";
    }

    private static string ReadKernelVersion(Dictionary<string, string> hostInfo)
    {
        if (hostInfo.TryGetValue("Kernel", out var kernel) && !string.IsNullOrWhiteSpace(kernel))
        {
            return kernel;
        }

        var uname = RunCommand("uname", "-r").Trim();
        return string.IsNullOrWhiteSpace(uname) ? RuntimeInformation.OSDescription : uname;
    }

    private static string ReadDesktopEnvironment()
    {
        var values = new[]
        {
            Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
            Environment.GetEnvironmentVariable("DESKTOP_SESSION"),
            Environment.GetEnvironmentVariable("GDMSESSION")
        }.Where(static value => !string.IsNullOrWhiteSpace(value));

        return string.Join(" / ", values) is { Length: > 0 } value ? value : "Not available";
    }

    private static string ReadCpuModel()
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2 && parts[0].Trim() == "model name")
                {
                    return parts[1].Trim();
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return "Not available";
    }

    private static string ReadCpuCounts()
    {
        var logical = Environment.ProcessorCount;
        var physical = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            string? physicalId = null;
            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                var parts = line.Split(':', 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                var key = parts[0].Trim();
                var value = parts[1].Trim();
                if (key == "physical id")
                {
                    physicalId = value;
                }
                else if (key == "core id")
                {
                    physical.Add($"{physicalId ?? "0"}:{value}");
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var cores = physical.Count > 0 ? physical.Count : logical;
        return $"{cores} cores / {logical} logical processors";
    }

    private static ulong ReadTotalRam()
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (!line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    continue;
                }

                var value = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
                return ulong.TryParse(value, out var kb) ? kb * 1024UL : 0;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return 0;
    }

    private static string ReadDiskSummary()
    {
        var disks = new List<string>();
        try
        {
            foreach (var path in SafeEnumerateDirectories("/sys/block", "*"))
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith("loop", StringComparison.Ordinal)
                    || name.StartsWith("ram", StringComparison.Ordinal)
                    || name.StartsWith("zram", StringComparison.Ordinal))
                {
                    continue;
                }

                var model = ReadTrimmed(Path.Combine(path, "device/model")) ?? name;
                var sectors = ParseUlong(ReadTrimmed(Path.Combine(path, "size")));
                disks.Add($"{model} ({FormatBytes(sectors * 512UL)})");
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return disks.Count == 0 ? "Not available" : string.Join("; ", disks);
    }

    private static string ReadGpuName()
    {
        var lspci = RunCommand("lspci", "");
        if (!string.IsNullOrWhiteSpace(lspci))
        {
            var lines = lspci.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(static line => line.Contains("VGA", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("3D controller", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("Display controller", StringComparison.OrdinalIgnoreCase))
                .Select(static line => line.Split(':', 3).LastOrDefault()?.Trim())
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
            if (lines.Length > 0)
            {
                return string.Join("; ", lines);
            }
        }

        try
        {
            var cards = SafeEnumerateDirectories("/sys/class/drm", "card*")
                .Select(static path => Path.GetFileName(path))
                .Where(static name => !string.IsNullOrWhiteSpace(name) && !name.Contains('-', StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return cards.Length == 0 ? "Not available" : string.Join(", ", cards);
        }
        catch (IOException)
        {
            return "Not available";
        }
        catch (UnauthorizedAccessException)
        {
            return "Not available";
        }
    }

    private static string ReadNetworkAdapters()
    {
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(static item => item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(static item => $"{item.Name} ({item.NetworkInterfaceType})")
                .ToArray();
            return adapters.Length == 0 ? "Not available" : string.Join("; ", adapters);
        }
        catch (NetworkInformationException)
        {
            return "Not available";
        }
    }

    private static string ReadBatteryStatus()
    {
        try
        {
            if (!Directory.Exists("/sys/class/power_supply"))
            {
                return "Not present";
            }

            foreach (var path in SafeEnumerateDirectories("/sys/class/power_supply", "*"))
            {
                if (ReadTrimmed(Path.Combine(path, "type")) != "Battery")
                {
                    continue;
                }

                var status = ReadTrimmed(Path.Combine(path, "status")) ?? "Unknown";
                var capacity = ReadTrimmed(Path.Combine(path, "capacity"));
                return string.IsNullOrWhiteSpace(capacity) ? status : $"{status}, {capacity}%";
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return "Not present";
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string directory, string pattern)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.EnumerateDirectories(directory, pattern).ToArray() : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (ArgumentException)
        {
            return [];
        }
    }

    private static string ReadBootTimeAndUptime()
    {
        try
        {
            var secondsText = File.ReadAllText("/proc/uptime").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!double.TryParse(secondsText, out var seconds))
            {
                return "Not available";
            }

            var uptime = TimeSpan.FromSeconds(seconds);
            var boot = DateTimeOffset.Now - uptime;
            return $"{boot:yyyy-MM-dd HH:mm} / {(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        }
        catch (IOException)
        {
            return "Not available";
        }
        catch (UnauthorizedAccessException)
        {
            return "Not available";
        }
    }

    private static string ReadBiosInfo()
    {
        var vendor = ReadTrimmed("/sys/class/dmi/id/bios_vendor");
        var version = ReadTrimmed("/sys/class/dmi/id/bios_version");
        var date = ReadTrimmed("/sys/class/dmi/id/bios_date");
        var product = ReadTrimmed("/sys/class/dmi/id/product_name");
        var values = new[] { product, vendor, version, date }.Where(static value => !string.IsNullOrWhiteSpace(value));
        return string.Join(" / ", values) is { Length: > 0 } value ? value : "Not available";
    }

    private static string ReadMachineType(Dictionary<string, string> hostInfo)
    {
        var chassis = hostInfo.GetValueOrDefault("Chassis");
        var hardwareVendor = hostInfo.GetValueOrDefault("Hardware Vendor");
        var hardwareModel = hostInfo.GetValueOrDefault("Hardware Model");
        var sysVendor = ReadTrimmed("/sys/class/dmi/id/sys_vendor");
        var product = ReadTrimmed("/sys/class/dmi/id/product_name");
        var machine = string.Join(" / ", new[] { chassis, hardwareVendor ?? sysVendor, hardwareModel ?? product }
            .Where(static value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(machine)
            ? RuntimeInformation.OSArchitecture.ToString()
            : $"{machine} ({RuntimeInformation.OSArchitecture})";
    }

    private static string? ReadTrimmed(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string RunCommand(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            return process is null ? string.Empty : process.StandardOutput.ReadToEnd();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static ulong ParseUlong(string? value)
    {
        return ulong.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }
}
