using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LinuxMintSystemMonitor;

internal sealed record ProcessRow(
    int Pid,
    string Name,
    string? IconPath,
    ProcessCategory Category,
    double CpuPercent,
    ulong ResidentBytes,
    double DiskReadBytesPerSecond,
    double DiskWriteBytesPerSecond,
    string NetworkIo,
    string User,
    string Status,
    string? ExecutablePath,
    string? AppGroupKey,
    string? AppGroupName,
    bool CanEndTask,
    string EndTaskReason);

internal enum ProcessCategory
{
    App,
    Background,
    System
}

internal sealed class ProcessMetricsReader
{
    private readonly Dictionary<int, ProcessCounters> _previous = new();
    private readonly Dictionary<uint, string> _users = ReadUsers();
    private readonly DesktopIconResolver _iconResolver = new();
    private ulong? _previousTotalCpu;
    private DateTimeOffset? _previousSampleTime;

    public IReadOnlyList<ProcessRow> Read()
    {
        var now = DateTimeOffset.UtcNow;
        var totalCpu = ReadTotalCpuJiffies();
        var elapsedSeconds = _previousSampleTime is null
            ? 1d
            : Math.Max(0.001d, (now - _previousSampleTime.Value).TotalSeconds);
        var totalCpuDelta = _previousTotalCpu is null || totalCpu < _previousTotalCpu.Value
            ? 0UL
            : totalCpu - _previousTotalCpu.Value;
        var processorCount = Math.Max(1, Environment.ProcessorCount);
        var rows = new List<ProcessRow>(256);
        var seen = new HashSet<int>();

        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(directory);
            if (!int.TryParse(name, out var pid))
            {
                continue;
            }

            var sample = TryReadProcess(pid, totalCpuDelta, processorCount, elapsedSeconds);
            if (sample is null)
            {
                continue;
            }

            rows.Add(sample.Value.Row);
            seen.Add(pid);
            _previous[pid] = sample.Value.Counters;
        }

        var stalePids = new List<int>();
        foreach (var pid in _previous.Keys)
        {
            if (!seen.Contains(pid))
            {
                stalePids.Add(pid);
            }
        }

        foreach (var pid in stalePids)
        {
            _previous.Remove(pid);
        }

        _previousTotalCpu = totalCpu;
        _previousSampleTime = now;
        return rows;
    }

    public static void EndTask(int pid)
    {
        if (pid <= 0)
        {
            return;
        }

        _ = kill(pid, SigTerm);
    }

    private ProcessSample? TryReadProcess(int pid, ulong totalCpuDelta, int processorCount, double elapsedSeconds)
    {
        try
        {
            var directory = Path.Combine("/proc", pid.ToString());
            var stat = ParseStat(File.ReadAllText(Path.Combine(directory, "stat")));
            var statusValues = ReadStatus(Path.Combine(directory, "status"));
            var io = ReadIo(Path.Combine(directory, "io"));
            var uid = statusValues.Uid;
            var user = uid is null ? "-" : _users.GetValueOrDefault(uid.Value, uid.Value.ToString());
            var cmdline = ReadCmdline(directory);
            var executablePath = ResolveExecutablePath(Path.Combine(directory, "exe"));
            var isKernelThread = string.IsNullOrWhiteSpace(cmdline.FirstArgument) && executablePath is null;
            var displayName = ReadDisplayName(cmdline.FirstArgument, stat.Name, isKernelThread);
            var appGroup = DetectAppGroup(displayName, executablePath, cmdline.Arguments);
            var iconPath = _iconResolver.Resolve(displayName, executablePath, appGroup?.Key, appGroup?.Name);
            var category = ClassifyProcess(displayName, stat.Name, executablePath, uid, isKernelThread, appGroup);
            var canEndTask = CanEndTask(uid, isKernelThread, out var endTaskReason);
            var totalProcessJiffies = stat.UserJiffies + stat.SystemJiffies;
            var previous = _previous.GetValueOrDefault(pid);
            var processDelta = previous.TotalJiffies == 0 || totalProcessJiffies < previous.TotalJiffies
                ? 0UL
                : totalProcessJiffies - previous.TotalJiffies;
            var cpuPercent = totalCpuDelta == 0
                ? 0d
                : processDelta / (double)totalCpuDelta * processorCount * 100d;
            var readRate = previous.ReadBytes == 0 || io.ReadBytes < previous.ReadBytes
                ? 0d
                : (io.ReadBytes - previous.ReadBytes) / elapsedSeconds;
            var writeRate = previous.WriteBytes == 0 || io.WriteBytes < previous.WriteBytes
                ? 0d
                : (io.WriteBytes - previous.WriteBytes) / elapsedSeconds;

            var row = new ProcessRow(
                pid,
                displayName,
                iconPath,
                category,
                Math.Max(0, cpuPercent),
                statusValues.RssBytes,
                readRate,
                writeRate,
                "-",
                user,
                MapStatus(stat.State),
                executablePath,
                appGroup?.Key,
                appGroup?.Name,
                canEndTask,
                endTaskReason);
            var counters = new ProcessCounters(totalProcessJiffies, io.ReadBytes, io.WriteBytes);
            return new ProcessSample(row, counters);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static ulong ReadTotalCpuJiffies()
    {
        var line = File.ReadLines("/proc/stat").FirstOrDefault(static value => value.StartsWith("cpu ", StringComparison.Ordinal));
        if (line is null)
        {
            return 0;
        }

        var total = 0UL;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i < parts.Length; i++)
        {
            total += ParseUlong(parts[i]);
        }

        return total;
    }

    private static ProcessStat ParseStat(string text)
    {
        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            throw new InvalidOperationException("Invalid process stat format.");
        }

        var name = text[(open + 1)..close];
        var fields = text[(close + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var state = fields.Length > 0 ? fields[0] : "?";
        var userJiffies = fields.Length > 11 ? ParseUlong(fields[11]) : 0;
        var systemJiffies = fields.Length > 12 ? ParseUlong(fields[12]) : 0;
        return new ProcessStat(name, state, userJiffies, systemJiffies);
    }

    private static ProcessStatus ReadStatus(string path)
    {
        ulong rssBytes = 0;
        uint? uid = null;

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("VmRSS:", StringComparison.Ordinal))
            {
                rssBytes = ParseStatusValue(line) * 1024UL;
            }
            else if (line.StartsWith("Uid:", StringComparison.Ordinal))
            {
                var value = ParseStatusValue(line);
                uid = value <= uint.MaxValue ? (uint)value : null;
            }
        }

        return new ProcessStatus(rssBytes, uid);
    }

    private static ProcessIo ReadIo(string path)
    {
        ulong readBytes = 0;
        ulong writeBytes = 0;

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("read_bytes:", StringComparison.Ordinal))
                {
                    readBytes = ParseStatusValue(line);
                }
                else if (line.StartsWith("write_bytes:", StringComparison.Ordinal))
                {
                    writeBytes = ParseStatusValue(line);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new ProcessIo(readBytes, writeBytes);
    }

    private static ProcessCmdline ReadCmdline(string directory)
    {
        try
        {
            var cmdline = File.ReadAllText(Path.Combine(directory, "cmdline"));
            var arguments = cmdline.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            var first = arguments.Length == 0 ? null : arguments[0];
            return new ProcessCmdline(first, arguments);
        }
        catch (IOException)
        {
            return new ProcessCmdline(null, Array.Empty<string>());
        }
        catch (UnauthorizedAccessException)
        {
            return new ProcessCmdline(null, Array.Empty<string>());
        }
    }

    private static string ReadDisplayName(string? firstArgument, string fallback, bool isKernelThread)
    {
        if (!string.IsNullOrWhiteSpace(firstArgument))
        {
            return Path.GetFileName(firstArgument);
        }

        if (string.IsNullOrWhiteSpace(fallback))
        {
            return "-";
        }

        return isKernelThread ? $"[{fallback}]" : fallback;
    }

    private static ProcessCategory ClassifyProcess(string displayName, string statName, string? executablePath, uint? uid, bool isKernelThread, AppGroup? appGroup)
    {
        if (isKernelThread || uid == 0 || IsSystemProcessName(displayName) || IsSystemExecutable(executablePath))
        {
            return ProcessCategory.System;
        }

        if (appGroup is not null || IsLikelyGuiApp(displayName, executablePath))
        {
            return ProcessCategory.App;
        }

        return ProcessCategory.Background;
    }

    private static AppGroup? DetectAppGroup(string displayName, string? executablePath, IReadOnlyList<string> arguments)
    {
        var name = displayName.Trim('[', ']').ToLowerInvariant();
        var path = executablePath?.ToLowerInvariant() ?? string.Empty;
        var hasChromiumTypeArg = HasArgumentContaining(arguments, "--type=renderer")
            || HasArgumentContaining(arguments, "--type=gpu")
            || HasArgumentContaining(arguments, "--type=utility")
            || HasArgumentContaining(arguments, "--type=zygote")
            || HasArgumentContaining(arguments, "crashpad");
        var hasVsCodeArg = HasArgumentContaining(arguments, "vscode");

        if (name.Contains("chrome", StringComparison.Ordinal)
            || name.Contains("chromium", StringComparison.Ordinal)
            || path.Contains("google-chrome", StringComparison.Ordinal)
            || path.Contains("chromium", StringComparison.Ordinal)
            || hasChromiumTypeArg)
        {
            var isChromium = name.Contains("chromium", StringComparison.Ordinal) || path.Contains("chromium", StringComparison.Ordinal);
            return new AppGroup(isChromium ? "chromium" : "google-chrome", isChromium ? "Chromium" : "Google Chrome");
        }

        if (name is "code" or "code-insiders" || path.Contains("/code", StringComparison.Ordinal) || hasVsCodeArg)
        {
            return new AppGroup("vscode", "Visual Studio Code");
        }

        if (name.Contains("firefox", StringComparison.Ordinal) || path.Contains("firefox", StringComparison.Ordinal))
        {
            return new AppGroup("firefox", "Firefox");
        }

        if (name is "gnome-terminal" or "mate-terminal" or "xfce4-terminal" or "konsole" or "tilix" or "alacritty" or "wezterm")
        {
            return new AppGroup("terminal", "Terminal");
        }

        if (name == "nemo" || path.EndsWith("/nemo", StringComparison.Ordinal))
        {
            return new AppGroup("nemo", "Nemo");
        }

        if (IsElectronProcess(name, path, arguments))
        {
            var label = string.IsNullOrWhiteSpace(displayName) ? "Electron App" : displayName;
            return new AppGroup($"electron:{label.ToLowerInvariant()}", label);
        }

        return null;
    }

    private static bool IsElectronProcess(string name, string path, IReadOnlyList<string> arguments)
    {
        return HasArgumentContaining(arguments, "electron")
            || HasArgumentContaining(arguments, "--type=renderer") && (path.Contains("/opt/", StringComparison.Ordinal) || path.Contains("/app/", StringComparison.Ordinal))
            || name is "discord" or "slack" or "spotify" or "teams" or "signal-desktop";
    }

    private static bool HasArgumentContaining(IReadOnlyList<string> arguments, string value)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanEndTask(uint? uid, bool isKernelThread, out string reason)
    {
        if (isKernelThread)
        {
            reason = "Kernel threads cannot be ended.";
            return false;
        }

        var currentUid = getuid();
        if (uid == 0 && currentUid != 0)
        {
            reason = "Root/system process.";
            return false;
        }

        if (uid is not null && uid.Value != currentUid && currentUid != 0)
        {
            reason = "Insufficient permission.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsSystemProcessName(string name)
    {
        var normalized = name.Trim('[', ']').ToLowerInvariant();
        if (normalized.StartsWith("kworker", StringComparison.Ordinal)
            || normalized.StartsWith("kthreadd", StringComparison.Ordinal)
            || normalized.StartsWith("rcu", StringComparison.Ordinal)
            || normalized.StartsWith("watchdog", StringComparison.Ordinal)
            || normalized.StartsWith("migration", StringComparison.Ordinal)
            || normalized.StartsWith("irq/", StringComparison.Ordinal)
            || normalized.StartsWith("systemd", StringComparison.Ordinal))
        {
            return true;
        }

        return normalized.EndsWith("d", StringComparison.Ordinal)
            && (normalized.Contains("dbus", StringComparison.Ordinal)
                || normalized.Contains("cron", StringComparison.Ordinal)
                || normalized.Contains("cups", StringComparison.Ordinal)
                || normalized.Contains("polkit", StringComparison.Ordinal)
                || normalized.Contains("accounts", StringComparison.Ordinal));
    }

    private static bool IsSystemExecutable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        return executablePath.StartsWith("/usr/sbin/", StringComparison.Ordinal)
            || executablePath.StartsWith("/sbin/", StringComparison.Ordinal)
            || executablePath.StartsWith("/lib/systemd/", StringComparison.Ordinal);
    }

    private static bool IsLikelyGuiApp(string displayName, string? executablePath)
    {
        var name = displayName.Trim('[', ']').ToLowerInvariant();
        string[] guiNames =
        [
            "chrome", "google-chrome", "chromium", "firefox", "code", "xed", "nemo",
            "gnome-terminal", "mate-terminal", "xfce4-terminal", "konsole", "tilix",
            "alacritty", "wezterm", "thunderbird", "libreoffice", "soffice.bin",
            "discord", "slack", "spotify", "steam", "vlc", "celluloid", "xviewer",
            "pix", "gimp", "inkscape", "blender", "obs", "virt-manager"
        ];

        if (guiNames.Any(gui => name == gui || name.StartsWith(gui + "-", StringComparison.Ordinal)))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        return executablePath.StartsWith("/opt/", StringComparison.Ordinal)
            || executablePath.StartsWith("/snap/", StringComparison.Ordinal)
            || executablePath.StartsWith("/var/lib/flatpak/", StringComparison.Ordinal);
    }

    private static string? ResolveExecutablePath(string path)
    {
        try
        {
            return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
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

    private static string MapStatus(string state)
    {
        return state switch
        {
            "R" => "Running",
            "S" => "Sleeping",
            "D" => "Waiting",
            "T" or "t" => "Stopped",
            "Z" => "Zombie",
            "I" => "Idle",
            _ => "Unknown"
        };
    }

    private static Dictionary<uint, string> ReadUsers()
    {
        var users = new Dictionary<uint, string>();
        try
        {
            foreach (var line in File.ReadLines("/etc/passwd"))
            {
                var parts = line.Split(':');
                if (parts.Length > 2 && uint.TryParse(parts[2], out var uid))
                {
                    users[uid] = parts[0];
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return users;
    }

    private static ulong ParseUlong(string? value)
    {
        return ulong.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static ulong ParseStatusValue(string line)
    {
        var colon = line.IndexOf(':');
        var start = colon < 0 ? 0 : colon + 1;
        while (start < line.Length && char.IsWhiteSpace(line[start]))
        {
            start++;
        }

        var end = start;
        while (end < line.Length && char.IsDigit(line[end]))
        {
            end++;
        }

        return end > start && ulong.TryParse(line.AsSpan(start, end - start), out var parsed)
            ? parsed
            : 0;
    }

    private const int SigTerm = 15;

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    [DllImport("libc")]
    private static extern uint getuid();

    private readonly record struct ProcessCounters(ulong TotalJiffies, ulong ReadBytes, ulong WriteBytes);
    private readonly record struct ProcessSample(ProcessRow Row, ProcessCounters Counters);
    private readonly record struct ProcessStat(string Name, string State, ulong UserJiffies, ulong SystemJiffies);
    private readonly record struct ProcessStatus(ulong RssBytes, uint? Uid);
    private readonly record struct ProcessIo(ulong ReadBytes, ulong WriteBytes);
    private readonly record struct ProcessCmdline(string? FirstArgument, IReadOnlyList<string> Arguments);
    private readonly record struct AppGroup(string Key, string Name);
}
