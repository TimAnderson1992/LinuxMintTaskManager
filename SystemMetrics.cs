using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LinuxMintSystemMonitor;

public sealed record SystemMetrics(
    double CpuPercent,
    IReadOnlyList<double> CpuCorePercents,
    string? CpuModelName,
    CpuDetails CpuDetails,
    MemoryDetails MemoryDetails,
    DiskDetails DiskDetails,
    NetworkDetails NetworkDetails,
    IReadOnlyList<GpuDetails> Gpus,
    double RamPercent,
    ulong RamUsedBytes,
    ulong RamTotalBytes,
    double DiskReadBytesPerSecond,
    double DiskWriteBytesPerSecond,
    double DiskActivePercent,
    double DiskAverageResponseMilliseconds,
    double NetworkReceiveBytesPerSecond,
    double NetworkTransmitBytesPerSecond,
    ulong NetworkTotalReceiveBytes,
    ulong NetworkTotalTransmitBytes,
    MetricReadErrors Errors);

public sealed record MetricReadErrors(
    string? Cpu,
    string? Memory,
    string? Disk,
    string? Network,
    string? Gpu);

public sealed record CpuDetails(
    string? ModelName,
    double? CurrentMhz,
    double? MaxMhz,
    int Processes,
    int Threads,
    long Handles,
    TimeSpan UpTime,
    int Sockets,
    int Cores,
    int LogicalProcessors,
    string Virtualization,
    string? L1Cache,
    string? L2Cache,
    string? L3Cache);

public sealed record MemoryDetails(
    ulong TotalBytes,
    ulong UsedBytes,
    ulong AvailableBytes,
    ulong CachedBytes,
    ulong CommitLimitBytes,
    ulong CommittedBytes,
    ulong? PagedPoolBytes,
    ulong? NonPagedPoolBytes,
    ulong SwapTotalBytes,
    ulong SwapFreeBytes,
    string Speed,
    string SlotsUsed,
    string FormFactor,
    string HardwareReserved);

public sealed record DiskDetails(
    string DeviceName,
    string DisplayName,
    string ModelName,
    ulong CapacityBytes,
    ulong FormattedBytes,
    bool? IsSystemDisk,
    bool? HasPageFile,
    string Type);

public sealed record NetworkDetails(
    string InterfaceName,
    string HeaderName,
    string Description,
    ulong? LinkSpeedBitsPerSecond,
    string IPv4Address,
    string IPv6Address,
    string MacAddress,
    string AdapterName,
    string DnsSuffix,
    string ConnectionType);

public sealed record GpuDetails(
    int Index,
    string Name,
    string DriverVersion,
    string PciDeviceName,
    string Vendor,
    string Source,
    string Status,
    string Note,
    double? UtilizationPercent,
    ulong? DedicatedMemoryUsedBytes,
    ulong? DedicatedMemoryTotalBytes,
    ulong? SharedMemoryBytes,
    double? TemperatureCelsius,
    double? EncoderUtilizationPercent,
    double? DecoderUtilizationPercent);

internal sealed class LinuxMetricsReader
{
    private CpuCounters? _previousCpu;
    private IReadOnlyList<CpuCounters>? _previousCpuCores;
    private DiskCounters? _previousDisk;
    private NetworkCounters? _previousNetwork;
    private CpuSample? _lastCpuSample;
    private CpuDetails? _lastCpuDetails;
    private DateTimeOffset _lastCpuDetailsRead = DateTimeOffset.MinValue;
    private MemoryCounters? _lastMemory;
    private DiskCounters? _lastDisk;
    private NetworkCounters? _lastNetwork;
    private IReadOnlyList<GpuDetails>? _lastGpus;
    private readonly GpuMetricsReader _gpuReader = new();
    private DateTimeOffset? _previousSampleTime;

    public SystemMetrics Read()
    {
        var now = DateTimeOffset.UtcNow;
        string? cpuError = null;
        string? memoryError = null;
        string? diskError = null;
        string? networkError = null;
        string? gpuError = null;

        CpuSample cpuSample;
        try
        {
            cpuSample = ReadCpuSample();
            _lastCpuSample = cpuSample;
        }
        catch (Exception ex) when (IsMetricReadException(ex))
        {
            cpuError = ex.Message;
            cpuSample = _lastCpuSample ?? new CpuSample(new CpuCounters(0, 0), Array.Empty<CpuCounters>());
        }

        CpuDetails cpuDetails;
        try
        {
            if (_lastCpuDetails is not null && now - _lastCpuDetailsRead < TimeSpan.FromSeconds(15))
            {
                cpuDetails = _lastCpuDetails;
            }
            else
            {
                cpuDetails = ReadCpuDetails(cpuSample.Cores.Count);
                _lastCpuDetails = cpuDetails;
                _lastCpuDetailsRead = now;
            }
        }
        catch (Exception ex) when (IsMetricReadException(ex))
        {
            cpuError = MergeError(cpuError, ex.Message);
            cpuDetails = _lastCpuDetails ?? BuildUnknownCpuDetails(cpuSample.Cores.Count);
        }

        MemoryCounters memory;
        try
        {
            memory = ReadMemory();
            _lastMemory = memory;
        }
        catch (Exception ex) when (IsMetricReadException(ex))
        {
            memoryError = ex.Message;
            memory = _lastMemory ?? BuildUnknownMemoryCounters();
        }

        DiskCounters disk;
        try
        {
            disk = ReadDiskCounters();
            _lastDisk = disk;
        }
        catch (Exception ex) when (IsMetricReadException(ex))
        {
            diskError = ex.Message;
            disk = _lastDisk ?? new DiskCounters(0, 0, 0, 0, 0, 0, BuildUnknownDiskDetails("Unknown"));
        }

        NetworkCounters network;
        try
        {
            network = ReadNetworkCounters();
            _lastNetwork = network;
        }
        catch (Exception ex) when (IsMetricReadException(ex))
        {
            networkError = ex.Message;
            network = _lastNetwork ?? new NetworkCounters("Unknown", 0, 0, BuildUnknownNetworkDetails());
        }

        IReadOnlyList<GpuDetails> gpus;
        try
        {
            gpus = _gpuReader.Read();
            _lastGpus = gpus;
        }
        catch (Exception ex) when (IsMetricReadException(ex))
        {
            gpuError = ex.Message;
            gpus = _lastGpus ?? [GpuMetricsReader.BuildUnknown(0)];
        }

        var elapsedSeconds = _previousSampleTime is null
            ? 1d
            : Math.Max(0.001d, (now - _previousSampleTime.Value).TotalSeconds);

        var cpuPercent = _previousCpu is null ? 0d : CalculateCpuPercent(_previousCpu.Value, cpuSample.Total);
        var corePercents = CalculateCorePercents(_previousCpuCores, cpuSample.Cores);
        var diskReadRate = _previousDisk is null ? 0d : DeltaPerSecond(_previousDisk.Value.ReadBytes, disk.ReadBytes, elapsedSeconds);
        var diskWriteRate = _previousDisk is null ? 0d : DeltaPerSecond(_previousDisk.Value.WriteBytes, disk.WriteBytes, elapsedSeconds);
        var diskActivePercent = _previousDisk is null ? 0d : CalculateDiskActivePercent(_previousDisk.Value, disk, elapsedSeconds);
        var diskAverageResponse = _previousDisk is null ? 0d : CalculateDiskAverageResponseMilliseconds(_previousDisk.Value, disk);
        var sameNetworkInterface = _previousNetwork is not null && _previousNetwork.Value.InterfaceName == network.InterfaceName;
        var networkReceiveRate = sameNetworkInterface ? DeltaPerSecond(_previousNetwork!.Value.ReceiveBytes, network.ReceiveBytes, elapsedSeconds) : 0d;
        var networkTransmitRate = sameNetworkInterface ? DeltaPerSecond(_previousNetwork!.Value.TransmitBytes, network.TransmitBytes, elapsedSeconds) : 0d;

        _previousCpu = cpuSample.Total;
        _previousCpuCores = cpuSample.Cores;
        _previousDisk = disk;
        _previousNetwork = network;
        _previousSampleTime = now;

        return new SystemMetrics(
            cpuPercent,
            corePercents,
            cpuDetails.ModelName,
            cpuDetails,
            memory.Details,
            disk.Details,
            network.Details,
            gpus,
            memory.PercentUsed,
            memory.UsedBytes,
            memory.TotalBytes,
            diskReadRate,
            diskWriteRate,
            diskActivePercent,
            diskAverageResponse,
            networkReceiveRate,
            networkTransmitRate,
            network.ReceiveBytes,
            network.TransmitBytes,
            new MetricReadErrors(cpuError, memoryError, diskError, networkError, gpuError));
    }

    private static CpuSample ReadCpuSample()
    {
        CpuCounters? total = null;
        var cores = new List<CpuCounters>();

        foreach (var line in File.ReadLines("/proc/stat"))
        {
            if (line.StartsWith("cpu ", StringComparison.Ordinal))
            {
                total = ParseCpuCounters(line);
                continue;
            }

            if (!line.StartsWith("cpu", StringComparison.Ordinal) || line.Length < 4 || !char.IsDigit(line[3]))
            {
                continue;
            }

            cores.Add(ParseCpuCounters(line));
        }

        return new CpuSample(total ?? new CpuCounters(0, 0), cores);
    }

    private static CpuCounters ParseCpuCounters(string line)
    {
        var values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(static value => ulong.TryParse(value, out var parsed) ? parsed : 0)
            .ToArray();

        var idle = Get(values, 3) + Get(values, 4);
        var total = values.Aggregate(0UL, static (sum, value) => sum + value);
        return new CpuCounters(idle, total);
    }

    private static IReadOnlyList<double> CalculateCorePercents(IReadOnlyList<CpuCounters>? previous, IReadOnlyList<CpuCounters> current)
    {
        var percents = new double[current.Count];
        if (previous is null)
        {
            return percents;
        }

        for (var i = 0; i < current.Count; i++)
        {
            percents[i] = i < previous.Count ? CalculateCpuPercent(previous[i], current[i]) : 0d;
        }

        return percents;
    }

    private static CpuDetails ReadCpuDetails(int logicalProcessors)
    {
        var cpuInfo = ReadCpuInfo();
        var modelName = FirstCpuInfoValue(cpuInfo, "model name");
        var currentMhz = ParseDouble(FirstCpuInfoValue(cpuInfo, "cpu MHz"));
        var maxMhz = ReadCpuFrequencyMhz("cpuinfo_max_freq") ?? ReadCpuFrequencyMhz("base_frequency");
        var physicalIds = ValuesForKey(cpuInfo, "physical id").Distinct(StringComparer.Ordinal).Count();
        var sockets = Math.Max(1, physicalIds);
        var cores = CalculatePhysicalCores(cpuInfo, sockets, logicalProcessors);
        var flags = FirstCpuInfoValue(cpuInfo, "flags") ?? string.Empty;
        var virtualization = DetectVirtualization(flags);
        var caches = ReadCpuCaches();
        var (processes, threads, handles) = ReadProcessCounts();

        return new CpuDetails(
            modelName,
            currentMhz,
            maxMhz,
            processes,
            threads,
            handles,
            ReadUptime(),
            sockets,
            cores,
            logicalProcessors,
            virtualization,
            caches.L1,
            caches.L2,
            caches.L3);
    }

    private static CpuDetails BuildUnknownCpuDetails(int logicalProcessors)
    {
        return new CpuDetails(
            null,
            null,
            null,
            0,
            0,
            0,
            TimeSpan.Zero,
            1,
            Math.Max(1, logicalProcessors),
            logicalProcessors,
            "Unknown",
            null,
            null,
            null);
    }

    private static Dictionary<string, List<string>> ReadCpuInfo()
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines("/proc/cpuinfo"))
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var key = parts[0].Trim();
            if (!values.TryGetValue(key, out var list))
            {
                list = new List<string>();
                values[key] = list;
            }

            list.Add(parts[1].Trim());
        }

        return values;
    }

    private static string? FirstCpuInfoValue(Dictionary<string, List<string>> cpuInfo, string key)
    {
        return cpuInfo.TryGetValue(key, out var values) ? values.FirstOrDefault() : null;
    }

    private static IEnumerable<string> ValuesForKey(Dictionary<string, List<string>> cpuInfo, string key)
    {
        return cpuInfo.TryGetValue(key, out var values) ? values : Enumerable.Empty<string>();
    }

    private static int CalculatePhysicalCores(Dictionary<string, List<string>> cpuInfo, int sockets, int logicalProcessors)
    {
        var physicalCorePairs = new HashSet<string>(StringComparer.Ordinal);
        var physicalIds = ValuesForKey(cpuInfo, "physical id").ToArray();
        var coreIds = ValuesForKey(cpuInfo, "core id").ToArray();

        for (var i = 0; i < Math.Min(physicalIds.Length, coreIds.Length); i++)
        {
            physicalCorePairs.Add($"{physicalIds[i]}:{coreIds[i]}");
        }

        if (physicalCorePairs.Count > 0)
        {
            return physicalCorePairs.Count;
        }

        if (int.TryParse(FirstCpuInfoValue(cpuInfo, "cpu cores"), out var coresPerSocket) && coresPerSocket > 0)
        {
            return coresPerSocket * sockets;
        }

        return Math.Max(1, logicalProcessors);
    }

    private static string DetectVirtualization(string flags)
    {
        if (flags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("hypervisor", StringComparer.Ordinal))
        {
            return "Virtual machine";
        }

        if (flags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(static flag => flag is "vmx" or "svm"))
        {
            return "Supported";
        }

        return "Not detected";
    }

    private static double? ReadCpuFrequencyMhz(string fileName)
    {
        var path = Path.Combine("/sys/devices/system/cpu/cpu0/cpufreq", fileName);
        if (!File.Exists(path) || !double.TryParse(File.ReadAllText(path).Trim(), out var khz))
        {
            return null;
        }

        return khz / 1000d;
    }

    private static (string? L1, string? L2, string? L3) ReadCpuCaches()
    {
        var root = "/sys/devices/system/cpu/cpu0/cache";
        if (!Directory.Exists(root))
        {
            return (null, null, null);
        }

        string? l1 = null;
        string? l2 = null;
        string? l3 = null;

        foreach (var directory in Directory.EnumerateDirectories(root, "index*"))
        {
            var level = ReadTrimmed(Path.Combine(directory, "level"));
            var type = ReadTrimmed(Path.Combine(directory, "type"));
            var size = ReadTrimmed(Path.Combine(directory, "size"));
            if (string.IsNullOrWhiteSpace(level) || string.IsNullOrWhiteSpace(size))
            {
                continue;
            }

            if (level == "1" && (type == "Data" || l1 is null))
            {
                l1 = size;
            }
            else if (level == "2")
            {
                l2 = size;
            }
            else if (level == "3")
            {
                l3 = size;
            }
        }

        return (l1, l2, l3);
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
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static (int Processes, int Threads, long Handles) ReadProcessCounts()
    {
        var processes = 0;
        var threads = 0;
        long handles = 0;

        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(directory);
            if (!name.All(char.IsDigit))
            {
                continue;
            }

            processes++;
            threads += CountDirectories(Path.Combine(directory, "task"));
            handles += CountFileSystemEntries(Path.Combine(directory, "fd"));
        }

        return (processes, threads, handles);
    }

    private static int CountDirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.EnumerateDirectories(path).Count() : 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static int CountFileSystemEntries(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.EnumerateFileSystemEntries(path).Count() : 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static TimeSpan ReadUptime()
    {
        var first = File.ReadAllText("/proc/uptime")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return double.TryParse(first, out var seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    }

    private static double? ParseDouble(string? value)
    {
        return double.TryParse(value, out var parsed) ? parsed : null;
    }

    private static double CalculateCpuPercent(CpuCounters previous, CpuCounters current)
    {
        var totalDelta = current.Total - previous.Total;
        if (totalDelta == 0)
        {
            return 0;
        }

        var idleDelta = current.Idle - previous.Idle;
        return ClampPercent((1d - idleDelta / (double)totalDelta) * 100d);
    }

    private static MemoryCounters ReadMemory()
    {
        var values = new Dictionary<string, ulong>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var number = parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (ulong.TryParse(number, out var kilobytes))
            {
                values[parts[0]] = kilobytes * 1024UL;
            }
        }

        var total = values.GetValueOrDefault("MemTotal");
        var available = values.GetValueOrDefault("MemAvailable");
        var used = total > available ? total - available : 0;
        var percent = total == 0 ? 0 : used / (double)total * 100d;
        var cached = values.GetValueOrDefault("Cached") + values.GetValueOrDefault("SReclaimable");
        var details = new MemoryDetails(
            total,
            used,
            available,
            cached,
            values.GetValueOrDefault("CommitLimit"),
            values.GetValueOrDefault("Committed_AS"),
            values.TryGetValue("SReclaimable", out var reclaimable) ? reclaimable : null,
            values.TryGetValue("SUnreclaim", out var unreclaimable) ? unreclaimable : null,
            values.GetValueOrDefault("SwapTotal"),
            values.GetValueOrDefault("SwapFree"),
            "Not available",
            "Not available",
            "Not available",
            "Not available");

        return new MemoryCounters(total, used, ClampPercent(percent), details);
    }

    private static MemoryCounters BuildUnknownMemoryCounters()
    {
        var details = new MemoryDetails(
            0,
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            0,
            0,
            "Not available",
            "Not available",
            "Not available",
            "Not available");

        return new MemoryCounters(0, 0, 0, details);
    }

    private static DiskCounters ReadDiskCounters()
    {
        var selectedDisk = SelectDiskDevice();
        var selectedDevices = selectedDisk is null
            ? GetPhysicalBlockDevices()
            : new HashSet<string>(StringComparer.Ordinal) { selectedDisk.Value.DeviceName };
        if (selectedDevices.Count == 0)
        {
            selectedDevices = GetDiskstatFallbackDevices();
        }

        DiskCounters? counters = null;

        foreach (var line in File.ReadLines("/proc/diskstats"))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 14 || !selectedDevices.Contains(parts[2]))
            {
                continue;
            }

            var current = ParseDiskCounters(parts, selectedDisk?.Details ?? BuildUnknownDiskDetails(parts[2]));
            counters = counters is null ? current : counters.Value.Add(current);
        }

        return counters ?? new DiskCounters(0, 0, 0, 0, 0, 0, selectedDisk?.Details ?? BuildUnknownDiskDetails("Unknown"));
    }

    private static SelectedDisk? SelectDiskDevice()
    {
        var physicalDevices = GetPhysicalBlockDevices();
        var rootBlockDevice = GetRootBlockDeviceName();
        var rootPhysicalDevice = rootBlockDevice is null ? null : FindPhysicalDevice(rootBlockDevice, physicalDevices);

        if (!string.IsNullOrWhiteSpace(rootPhysicalDevice))
        {
            return BuildSelectedDisk(rootPhysicalDevice, isSystemDisk: true);
        }

        var firstPhysical = physicalDevices.Order(StringComparer.Ordinal).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstPhysical)
            ? null
            : BuildSelectedDisk(firstPhysical, IsRootOnDevice(firstPhysical) ? true : null);
    }

    private static DiskCounters ParseDiskCounters(string[] parts, DiskDetails details)
    {
        const ulong sectorSize = 512;
        var readSectors = ParseUlong(parts[5]);
        var writtenSectors = ParseUlong(parts[9]);
        var readsCompleted = ParseUlong(parts[3]);
        var writesCompleted = ParseUlong(parts[7]);
        var ioMilliseconds = ParseUlong(parts[12]);
        var weightedIoMilliseconds = ParseUlong(parts[13]);

        return new DiskCounters(
            readSectors * sectorSize,
            writtenSectors * sectorSize,
            readsCompleted,
            writesCompleted,
            ioMilliseconds,
            weightedIoMilliseconds,
            details);
    }

    private static SelectedDisk BuildSelectedDisk(string deviceName, bool? isSystemDisk)
    {
        return new SelectedDisk(deviceName, BuildDiskDetails(deviceName, isSystemDisk));
    }

    private static DiskDetails BuildDiskDetails(string deviceName, bool? isSystemDisk)
    {
        var blockPath = Path.Combine("/sys/block", deviceName);
        var model = ReadDiskModel(blockPath, deviceName);
        var capacity = ReadDiskCapacity(blockPath);
        var formatted = isSystemDisk == true ? ReadRootFormattedBytes() : capacity;
        var type = ReadDiskType(blockPath);
        var hasPageFile = DetectSwapOnDevice(deviceName);

        return new DiskDetails(
            deviceName,
            $"Disk 0 ({deviceName})",
            model,
            capacity,
            formatted,
            isSystemDisk,
            hasPageFile,
            type);
    }

    private static DiskDetails BuildUnknownDiskDetails(string deviceName)
    {
        return new DiskDetails(deviceName, deviceName, "Disk model unavailable", 0, 0, null, null, "Unknown");
    }

    private static HashSet<string> GetPhysicalBlockDevices()
    {
        var devices = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateDirectories("/sys/block"))
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var devicePath = Path.Combine(path, "device");
            if (Directory.Exists(devicePath) && !IsIgnoredBlockDevice(name))
            {
                devices.Add(name);
            }
        }

        return devices;
    }

    private static bool IsIgnoredBlockDevice(string name)
    {
        return name.StartsWith("loop", StringComparison.Ordinal)
            || name.StartsWith("ram", StringComparison.Ordinal)
            || name.StartsWith("zram", StringComparison.Ordinal);
    }

    private static HashSet<string> GetDiskstatFallbackDevices()
    {
        var devices = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines("/proc/diskstats"))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 14 && !IsIgnoredBlockDevice(parts[2]))
            {
                devices.Add(parts[2]);
            }
        }

        return devices;
    }

    private static string? GetRootBlockDeviceName()
    {
        foreach (var line in File.ReadLines("/proc/mounts"))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[1] == "/")
            {
                return Path.GetFileName(ResolveDevicePath(parts[0]));
            }
        }

        return null;
    }

    private static string ResolveDevicePath(string source)
    {
        if (!source.StartsWith("/dev/", StringComparison.Ordinal))
        {
            return source;
        }

        try
        {
            var info = new FileInfo(source);
            return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? source;
        }
        catch (IOException)
        {
            return source;
        }
        catch (UnauthorizedAccessException)
        {
            return source;
        }
    }

    private static string? FindPhysicalDevice(string blockDevice, HashSet<string> physicalDevices)
    {
        if (physicalDevices.Contains(blockDevice))
        {
            return blockDevice;
        }

        foreach (var physicalDevice in physicalDevices)
        {
            if (Directory.Exists(Path.Combine("/sys/block", physicalDevice, blockDevice)))
            {
                return physicalDevice;
            }
        }

        return null;
    }

    private static bool IsRootOnDevice(string deviceName)
    {
        var root = GetRootBlockDeviceName();
        return root is not null && (root == deviceName || Directory.Exists(Path.Combine("/sys/block", deviceName, root)));
    }

    private static string ReadDiskModel(string blockPath, string deviceName)
    {
        var vendor = ReadTrimmed(Path.Combine(blockPath, "device/vendor"));
        var model = ReadTrimmed(Path.Combine(blockPath, "device/model"));
        var values = new[] { vendor, model }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .ToArray();

        return values.Length == 0 ? deviceName : string.Join(" ", values);
    }

    private static ulong ReadDiskCapacity(string blockPath)
    {
        var sectorCount = ParseUlong(ReadTrimmed(Path.Combine(blockPath, "size")) ?? "0");
        var logicalBlockSize = ParseUlong(ReadTrimmed(Path.Combine(blockPath, "queue/logical_block_size")) ?? "512");
        return sectorCount * Math.Max(1UL, logicalBlockSize);
    }

    private static ulong ReadRootFormattedBytes()
    {
        try
        {
            return (ulong)Math.Max(0, new DriveInfo("/").TotalSize);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string ReadDiskType(string blockPath)
    {
        var rotational = ReadTrimmed(Path.Combine(blockPath, "queue/rotational"));
        return rotational switch
        {
            "0" => "SSD",
            "1" => "HDD",
            _ => "Unknown"
        };
    }

    private static bool? DetectSwapOnDevice(string deviceName)
    {
        if (!File.Exists("/proc/swaps"))
        {
            return null;
        }

        foreach (var line in File.ReadLines("/proc/swaps").Skip(1))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var swapName = Path.GetFileName(ResolveDevicePath(parts[0]));
            if (swapName == deviceName || Directory.Exists(Path.Combine("/sys/block", deviceName, swapName)))
            {
                return true;
            }
        }

        return false;
    }

    private static double CalculateDiskActivePercent(DiskCounters previous, DiskCounters current, double elapsedSeconds)
    {
        if (current.IoMilliseconds < previous.IoMilliseconds)
        {
            return 0;
        }

        return ClampPercent((current.IoMilliseconds - previous.IoMilliseconds) / (elapsedSeconds * 1000d) * 100d);
    }

    private static double CalculateDiskAverageResponseMilliseconds(DiskCounters previous, DiskCounters current)
    {
        var previousOperations = previous.ReadOperations + previous.WriteOperations;
        var currentOperations = current.ReadOperations + current.WriteOperations;
        if (currentOperations <= previousOperations || current.WeightedIoMilliseconds < previous.WeightedIoMilliseconds)
        {
            return 0;
        }

        return (current.WeightedIoMilliseconds - previous.WeightedIoMilliseconds) / (double)(currentOperations - previousOperations);
    }

    private static NetworkCounters ReadNetworkCounters()
    {
        var samples = new List<NetworkInterfaceSample>();

        foreach (var line in File.ReadLines("/proc/net/dev").Skip(2))
        {
            var parts = line.Split(new[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 17)
            {
                continue;
            }

            var interfaceName = parts[0];
            samples.Add(new NetworkInterfaceSample(
                interfaceName,
                ParseUlong(parts[1]),
                ParseUlong(parts[9])));
        }

        var selected = SelectNetworkInterface(samples);
        return selected is null
            ? new NetworkCounters("Unknown", 0, 0, BuildUnknownNetworkDetails())
            : new NetworkCounters(
                selected.Value.InterfaceName,
                selected.Value.ReceiveBytes,
                selected.Value.TransmitBytes,
                BuildNetworkDetails(selected.Value.InterfaceName));
    }

    private static NetworkInterfaceSample? SelectNetworkInterface(IReadOnlyList<NetworkInterfaceSample> samples)
    {
        if (samples.Count == 0)
        {
            return null;
        }

        var realInterfaces = samples
            .Where(static sample => sample.InterfaceName != "lo")
            .ToArray();
        var candidates = realInterfaces.Length > 0 ? realInterfaces : samples;

        var active = candidates
            .Where(static sample => IsNetworkInterfaceUp(sample.InterfaceName))
            .OrderByDescending(static sample => IsPreferredNetworkType(sample.InterfaceName))
            .ThenByDescending(static sample => sample.ReceiveBytes + sample.TransmitBytes)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(active.InterfaceName))
        {
            return active;
        }

        return candidates
            .OrderByDescending(static sample => sample.ReceiveBytes + sample.TransmitBytes)
            .FirstOrDefault();
    }

    private static bool IsNetworkInterfaceUp(string interfaceName)
    {
        var operState = ReadTrimmed(Path.Combine("/sys/class/net", interfaceName, "operstate"));
        return operState is "up" or "unknown";
    }

    private static bool IsPreferredNetworkType(string interfaceName)
    {
        var path = Path.Combine("/sys/class/net", interfaceName);
        return Directory.Exists(Path.Combine(path, "wireless"))
            || interfaceName.StartsWith("en", StringComparison.Ordinal)
            || interfaceName.StartsWith("eth", StringComparison.Ordinal)
            || interfaceName.StartsWith("wl", StringComparison.Ordinal);
    }

    private static NetworkDetails BuildNetworkDetails(string interfaceName)
    {
        var networkInterface = SafeGetNetworkInterface(interfaceName);
        var properties = SafeGetIPProperties(networkInterface);
        var linkSpeed = ReadNetworkLinkSpeed(interfaceName) ?? ReadNetworkInterfaceSpeed(networkInterface);
        var connectionType = DetectConnectionType(interfaceName, networkInterface);
        var description = SafeNetworkDescription(networkInterface);

        return new NetworkDetails(
            interfaceName,
            connectionType is "Ethernet" or "Wi-Fi" ? connectionType : interfaceName,
            string.IsNullOrWhiteSpace(description) ? interfaceName : description,
            linkSpeed,
            FirstAddress(properties, AddressFamily.InterNetwork),
            FirstAddress(properties, AddressFamily.InterNetworkV6),
            ReadNetworkMacAddress(interfaceName, networkInterface),
            interfaceName,
            SafeDnsSuffix(properties),
            connectionType);
    }

    private static NetworkInterface? SafeGetNetworkInterface(string interfaceName)
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(item => item.Name == interfaceName);
        }
        catch (NetworkInformationException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static IPInterfaceProperties? SafeGetIPProperties(NetworkInterface? networkInterface)
    {
        try
        {
            return networkInterface?.GetIPProperties();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? SafeNetworkDescription(NetworkInterface? networkInterface)
    {
        try
        {
            return networkInterface?.Description;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static NetworkDetails BuildUnknownNetworkDetails()
    {
        return new NetworkDetails(
            "Unknown",
            "Network",
            "Adapter unavailable",
            null,
            "Not available",
            "Not available",
            "Not available",
            "Unknown",
            "Not available",
            "Unknown");
    }

    private static ulong? ReadNetworkLinkSpeed(string interfaceName)
    {
        var speedText = ReadTrimmed(Path.Combine("/sys/class/net", interfaceName, "speed"));
        if (!long.TryParse(speedText, out var megabitsPerSecond) || megabitsPerSecond <= 0)
        {
            return null;
        }

        return (ulong)megabitsPerSecond * 1_000_000UL;
    }

    private static ulong? ReadNetworkInterfaceSpeed(NetworkInterface? networkInterface)
    {
        try
        {
            if (networkInterface is null || networkInterface.Speed <= 0)
            {
                return null;
            }

            return (ulong)networkInterface.Speed;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string FirstAddress(IPInterfaceProperties? properties, AddressFamily family)
    {
        try
        {
            var address = properties?.UnicastAddresses
                .Select(static item => item.Address)
                .FirstOrDefault(address => address.AddressFamily == family && !IPAddress.IsLoopback(address));

            return address is null ? "Not available" : address.ToString();
        }
        catch (NetworkInformationException)
        {
            return "Not available";
        }
        catch (InvalidOperationException)
        {
            return "Not available";
        }
    }

    private static string SafeDnsSuffix(IPInterfaceProperties? properties)
    {
        try
        {
            return string.IsNullOrWhiteSpace(properties?.DnsSuffix) ? "Not available" : properties!.DnsSuffix;
        }
        catch (NetworkInformationException)
        {
            return "Not available";
        }
        catch (InvalidOperationException)
        {
            return "Not available";
        }
    }

    private static string ReadNetworkMacAddress(string interfaceName, NetworkInterface? networkInterface)
    {
        var sysAddress = ReadTrimmed(Path.Combine("/sys/class/net", interfaceName, "address"));
        if (!string.IsNullOrWhiteSpace(sysAddress))
        {
            return sysAddress.ToUpperInvariant();
        }

        try
        {
            var bytes = networkInterface?.GetPhysicalAddress().GetAddressBytes();
            return bytes is { Length: > 0 }
                ? string.Join(":", bytes.Select(static value => value.ToString("X2")))
                : "Not available";
        }
        catch (NetworkInformationException)
        {
            return "Not available";
        }
        catch (InvalidOperationException)
        {
            return "Not available";
        }
    }

    private static string DetectConnectionType(string interfaceName, NetworkInterface? networkInterface)
    {
        var interfaceType = SafeNetworkInterfaceType(networkInterface);
        if (Directory.Exists(Path.Combine("/sys/class/net", interfaceName, "wireless"))
            || interfaceType == NetworkInterfaceType.Wireless80211)
        {
            return "Wi-Fi";
        }

        if (interfaceType == NetworkInterfaceType.Ethernet
            || interfaceName.StartsWith("en", StringComparison.Ordinal)
            || interfaceName.StartsWith("eth", StringComparison.Ordinal))
        {
            return "Ethernet";
        }

        return "Unknown";
    }

    private static NetworkInterfaceType? SafeNetworkInterfaceType(NetworkInterface? networkInterface)
    {
        try
        {
            return networkInterface?.NetworkInterfaceType;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static double DeltaPerSecond(ulong previous, ulong current, double elapsedSeconds)
    {
        if (current < previous)
        {
            return 0;
        }

        return (current - previous) / elapsedSeconds;
    }

    private static bool IsMetricReadException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or PlatformNotSupportedException
            or NetworkInformationException;
    }

    private static string MergeError(string? existing, string next)
    {
        return string.IsNullOrWhiteSpace(existing) ? next : $"{existing}; {next}";
    }

    private static ulong ParseUlong(string value)
    {
        return ulong.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static ulong Get(ulong[] values, int index)
    {
        return index < values.Length ? values[index] : 0;
    }

    private static double ClampPercent(double value)
    {
        return Math.Clamp(value, 0d, 100d);
    }

    private readonly record struct CpuCounters(ulong Idle, ulong Total);
    private sealed record CpuSample(CpuCounters Total, IReadOnlyList<CpuCounters> Cores);
    private readonly record struct MemoryCounters(ulong TotalBytes, ulong UsedBytes, double PercentUsed, MemoryDetails Details);
    private readonly record struct DiskCounters(
        ulong ReadBytes,
        ulong WriteBytes,
        ulong ReadOperations,
        ulong WriteOperations,
        ulong IoMilliseconds,
        ulong WeightedIoMilliseconds,
        DiskDetails Details)
    {
        public DiskCounters Add(DiskCounters other)
        {
            return this with
            {
                ReadBytes = ReadBytes + other.ReadBytes,
                WriteBytes = WriteBytes + other.WriteBytes,
                ReadOperations = ReadOperations + other.ReadOperations,
                WriteOperations = WriteOperations + other.WriteOperations,
                IoMilliseconds = IoMilliseconds + other.IoMilliseconds,
                WeightedIoMilliseconds = WeightedIoMilliseconds + other.WeightedIoMilliseconds
            };
        }
    }

    private readonly record struct SelectedDisk(string DeviceName, DiskDetails Details);
    private readonly record struct NetworkInterfaceSample(string InterfaceName, ulong ReceiveBytes, ulong TransmitBytes);
    private readonly record struct NetworkCounters(string InterfaceName, ulong ReceiveBytes, ulong TransmitBytes, NetworkDetails Details);
}
