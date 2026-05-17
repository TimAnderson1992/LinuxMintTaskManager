using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace LinuxMintSystemMonitor;

internal sealed class GpuMetricsReader
{
    public IReadOnlyList<GpuDetails> Read()
    {
        var detected = ReadDetectedGpus();
        var nvidiaMetrics = TryReadNvidiaMetrics();

        if (detected.Count == 0 && nvidiaMetrics.Count > 0)
        {
            return Reindex(nvidiaMetrics);
        }

        var merged = detected.Select(gpu => ApplyLiveMetrics(gpu, nvidiaMetrics)).ToList();
        foreach (var metricGpu in nvidiaMetrics)
        {
            if (!merged.Any(gpu => gpu.Vendor == "NVIDIA" && NamesMatch(gpu.Name, metricGpu.Name)))
            {
                merged.Add(metricGpu);
            }
        }

        if (merged.Count == 0)
        {
            merged.Add(BuildUnknown(0));
        }

        return Reindex(merged.OrderBy(static gpu => VendorSort(gpu.Vendor)).ThenBy(static gpu => gpu.Name).ToArray());
    }

    public static GpuDetails BuildUnknown(int index)
    {
        return new GpuDetails(
            index,
            "GPU not detected",
            "Not available",
            "Not available",
            "Unknown",
            "Unavailable",
            "Not available",
            string.Empty,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
    }

    private static IReadOnlyList<GpuDetails> ReadDetectedGpus()
    {
        var gpus = ReadLspciGpus().ToList();
        foreach (var sysfsGpu in ReadSysfsGpus())
        {
            var sameVendorIndexes = gpus
                .Select((gpu, index) => (gpu, index))
                .Where(item => item.gpu.Vendor == sysfsGpu.Vendor)
                .Select(static item => item.index)
                .ToArray();
            var existingIndex = gpus.FindIndex(gpu => gpu.Vendor == sysfsGpu.Vendor && NamesMatch(gpu.Name, sysfsGpu.Name));
            if (existingIndex < 0 && sameVendorIndexes.Length == 1)
            {
                existingIndex = sameVendorIndexes[0];
            }

            if (existingIndex >= 0)
            {
                var current = gpus[existingIndex];
                gpus[existingIndex] = current with
                {
                    DriverVersion = sysfsGpu.DriverVersion,
                    Source = current.Source.Contains("sysfs", StringComparison.Ordinal) ? current.Source : $"{current.Source} / sysfs",
                    Status = sysfsGpu.Status,
                    Note = sysfsGpu.Note,
                    UtilizationPercent = sysfsGpu.UtilizationPercent,
                    TemperatureCelsius = sysfsGpu.TemperatureCelsius,
                    DedicatedMemoryUsedBytes = sysfsGpu.DedicatedMemoryUsedBytes,
                    DedicatedMemoryTotalBytes = sysfsGpu.DedicatedMemoryTotalBytes
                };
            }
            else
            {
                gpus.Add(sysfsGpu);
            }
        }

        return gpus;
    }

    private static GpuDetails ApplyLiveMetrics(GpuDetails detected, IReadOnlyList<GpuDetails> nvidiaMetrics)
    {
        if (detected.Vendor != "NVIDIA")
        {
            return detected.Vendor == "AMD"
                ? ApplyAmdStatus(detected)
                : detected;
        }

        var live = nvidiaMetrics.FirstOrDefault(metric => NamesMatch(metric.Name, detected.Name)) ?? nvidiaMetrics.FirstOrDefault();
        if (live is not null)
        {
            return detected with
            {
                Name = live.Name,
                DriverVersion = live.DriverVersion,
                Source = "nvidia-smi",
                Status = "Active",
                Note = string.Empty,
                UtilizationPercent = live.UtilizationPercent,
                DedicatedMemoryUsedBytes = live.DedicatedMemoryUsedBytes,
                DedicatedMemoryTotalBytes = live.DedicatedMemoryTotalBytes,
                TemperatureCelsius = live.TemperatureCelsius
            };
        }

        var mode = DetectNvidiaMode();
        return detected with
        {
            Status = mode.Contains("On-Demand", StringComparison.Ordinal) ? "Sleeping / On-Demand" : "Unavailable until active",
            Note = "NVIDIA metrics may appear when an app is using the NVIDIA GPU."
        };
    }

    private static GpuDetails ApplyAmdStatus(GpuDetails detected)
    {
        var hasMetrics = detected.UtilizationPercent is not null
            || detected.DedicatedMemoryUsedBytes is not null
            || detected.DedicatedMemoryTotalBytes is not null
            || detected.TemperatureCelsius is not null;

        return detected with
        {
            Status = hasMetrics ? "Available" : "Metrics unavailable",
            Note = hasMetrics
                ? "AMD/Radeon metrics are read from Linux sysfs amdgpu data when exposed by the kernel."
                : "AMD/Radeon detected. This kernel or driver is not exposing live GPU metrics."
        };
    }

    private static IReadOnlyList<GpuDetails> TryReadNvidiaMetrics()
    {
        var output = RunCommand(
            "nvidia-smi",
            "--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu,driver_version --format=csv,noheader,nounits",
            timeoutMilliseconds: 1500);

        if (string.IsNullOrWhiteSpace(output) || output.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var gpus = new List<GpuDetails>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(',').Select(static part => part.Trim()).ToArray();
            var name = Get(parts, 0, "NVIDIA GPU");
            if (LooksLikeError(name))
            {
                continue;
            }

            ulong? memoryUsed = ParseNullableUlong(Get(parts, 2, string.Empty)) is { } used ? used * 1024UL * 1024UL : null;
            ulong? memoryTotal = ParseNullableUlong(Get(parts, 3, string.Empty)) is { } total ? total * 1024UL * 1024UL : null;
            gpus.Add(new GpuDetails(
                gpus.Count,
                name,
                Get(parts, 5, "Not available"),
                name,
                "NVIDIA",
                "nvidia-smi",
                "Active",
                string.Empty,
                ParseNullableDouble(Get(parts, 1, string.Empty)),
                memoryUsed,
                memoryTotal,
                null,
                ParseNullableDouble(Get(parts, 4, string.Empty)),
                null,
                null));
        }

        return gpus;
    }

    private static IEnumerable<GpuDetails> ReadLspciGpus()
    {
        var output = RunCommand("lspci", "", timeoutMilliseconds: 1000);
        if (string.IsNullOrWhiteSpace(output))
        {
            yield break;
        }

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsGpuPciLine(line))
            {
                continue;
            }

            var name = CleanPciName(line);
            if (LooksLikeError(name))
            {
                continue;
            }

            var vendor = DetectVendorFromName(name);
            yield return new GpuDetails(
                0,
                SimplifyGpuName(name),
                "Not available",
                name,
                vendor,
                "lspci",
                BuildDetectedStatus(vendor),
                BuildDetectedNote(vendor, hasAmdMetrics: false),
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }
    }

    private static IEnumerable<GpuDetails> ReadSysfsGpus()
    {
        if (!Directory.Exists("/sys/class/drm"))
        {
            yield break;
        }

        foreach (var card in SafeEnumerateDirectories("/sys/class/drm", "card*"))
        {
            var cardName = Path.GetFileName(card);
            if (string.IsNullOrWhiteSpace(cardName) || cardName.Contains('-', StringComparison.Ordinal))
            {
                continue;
            }

            var devicePath = Path.Combine(card, "device");
            var vendorId = ReadTrimmed(Path.Combine(devicePath, "vendor"));
            var deviceId = ReadTrimmed(Path.Combine(devicePath, "device"));
            if (string.IsNullOrWhiteSpace(vendorId) && string.IsNullOrWhiteSpace(deviceId))
            {
                continue;
            }

            var vendor = VendorName(vendorId);
            var utilization = vendor == "AMD" ? TryReadAmdUtilization(devicePath) : null;
            var memoryUsed = vendor == "AMD" ? TryReadAmdVramUsed(devicePath) : null;
            var memoryTotal = vendor == "AMD" ? TryReadAmdVramTotal(devicePath) : null;
            var temperature = TryReadHwmonTemperature(devicePath);
            var hasAmdMetrics = utilization is not null || memoryUsed is not null || memoryTotal is not null || temperature is not null;
            yield return new GpuDetails(
                0,
                BuildSysfsGpuName(vendor, deviceId),
                ReadDriverName(devicePath),
                cardName,
                vendor,
                "sysfs",
                BuildDetectedStatus(vendor, hasAmdMetrics),
                BuildDetectedNote(vendor, hasAmdMetrics),
                utilization,
                memoryUsed,
                memoryTotal,
                null,
                temperature,
                null,
                null);
        }
    }

    private static IReadOnlyList<GpuDetails> Reindex(IReadOnlyList<GpuDetails> gpus)
    {
        return gpus.Select((gpu, index) => gpu with { Index = index }).ToArray();
    }

    private static bool IsGpuPciLine(string line)
    {
        return line.Contains("VGA", StringComparison.OrdinalIgnoreCase)
            || line.Contains("3D controller", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Display controller", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanPciName(string line)
    {
        var controllerIndex = line.IndexOf("controller:", StringComparison.OrdinalIgnoreCase);
        if (controllerIndex >= 0)
        {
            return line[(controllerIndex + "controller:".Length)..].Trim();
        }

        var bridgeIndex = line.IndexOf("VGA compatible controller:", StringComparison.OrdinalIgnoreCase);
        if (bridgeIndex >= 0)
        {
            return line[(bridgeIndex + "VGA compatible controller:".Length)..].Trim();
        }

        return line.Trim();
    }

    private static string SimplifyGpuName(string name)
    {
        return name
            .Replace("Corporation", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Advanced Micro Devices, Inc. [AMD/ATI]", "AMD", StringComparison.OrdinalIgnoreCase)
            .Replace("Intel Corporation", "Intel", StringComparison.OrdinalIgnoreCase)
            .Replace("NVIDIA Corporation", "NVIDIA", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static bool NamesMatch(string first, string second)
    {
        var a = NormalizeName(first);
        var b = NormalizeName(second);
        return a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
    }

    private static string NormalizeName(string value)
    {
        return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    private static int VendorSort(string vendor)
    {
        return vendor switch
        {
            "Intel" => 0,
            "AMD" => 1,
            "NVIDIA" => 2,
            _ => 3
        };
    }

    private static string DetectNvidiaMode()
    {
        var prime = RunCommand("prime-select", "query", timeoutMilliseconds: 800).Trim();
        if (prime.Contains("on-demand", StringComparison.OrdinalIgnoreCase))
        {
            return "Sleeping / On-Demand";
        }

        return "Unavailable until active";
    }

    private static string BuildDetectedStatus(string vendor, bool hasAmdMetrics = false)
    {
        return vendor switch
        {
            "NVIDIA" => DetectNvidiaMode(),
            "AMD" => hasAmdMetrics ? "Available" : "Metrics unavailable",
            "Intel" => "Detected",
            "Unknown" => "Detected",
            _ => "Detected"
        };
    }

    private static string BuildDetectedNote(string vendor, bool hasAmdMetrics)
    {
        return vendor switch
        {
            "NVIDIA" => "NVIDIA metrics may appear when an app is using the NVIDIA GPU.",
            "AMD" when hasAmdMetrics => "AMD/Radeon metrics are read from Linux sysfs amdgpu data when exposed by the kernel.",
            "AMD" => "AMD/Radeon detected. This kernel or driver is not exposing live GPU metrics.",
            "Intel" => "Intel GPU detected. Live utilization and VRAM metrics are not exposed on all drivers.",
            _ => "GPU detected. Live metrics are not available on this hardware or driver."
        };
    }

    private static string BuildSysfsGpuName(string vendor, string? deviceId)
    {
        var suffix = string.IsNullOrWhiteSpace(deviceId) ? string.Empty : $" {deviceId}";
        return vendor switch
        {
            "AMD" => $"AMD/Radeon GPU{suffix}",
            "NVIDIA" => $"NVIDIA GPU{suffix}",
            "Intel" => $"Intel GPU{suffix}",
            _ => $"GPU{suffix}"
        };
    }

    private static bool LooksLikeError(string value)
    {
        return value.Contains("NVIDIA-SMI has failed", StringComparison.OrdinalIgnoreCase)
            || value.Contains("couldn't communicate", StringComparison.OrdinalIgnoreCase)
            || value.Contains("failed because", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadDriverName(string devicePath)
    {
        try
        {
            var driverLink = Path.Combine(devicePath, "driver");
            var target = new FileInfo(driverLink).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            return string.IsNullOrWhiteSpace(target) ? "Not available" : Path.GetFileName(target);
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

    private static double? TryReadHwmonTemperature(string devicePath)
    {
        foreach (var hwmon in SafeEnumerateDirectories(Path.Combine(devicePath, "hwmon"), "hwmon*"))
        {
            foreach (var input in SafeEnumerateFiles(hwmon, "temp*_input"))
            {
                if (double.TryParse(ReadTrimmed(input), NumberStyles.Float, CultureInfo.InvariantCulture, out var millidegrees))
                {
                    return millidegrees / 1000d;
                }
            }
        }

        return null;
    }

    private static ulong? TryReadAmdVramUsed(string devicePath)
    {
        return ParseNullableUlong(ReadTrimmed(Path.Combine(devicePath, "mem_info_vram_used")));
    }

    private static ulong? TryReadAmdVramTotal(string devicePath)
    {
        return ParseNullableUlong(ReadTrimmed(Path.Combine(devicePath, "mem_info_vram_total")));
    }

    private static double? TryReadAmdUtilization(string devicePath)
    {
        return ParseNullableDouble(ReadTrimmed(Path.Combine(devicePath, "gpu_busy_percent")));
    }

    private static string VendorName(string? vendorId)
    {
        return vendorId?.ToLowerInvariant() switch
        {
            "0x10de" => "NVIDIA",
            "0x8086" => "Intel",
            "0x1002" => "AMD",
            "0x1022" => "AMD",
            _ => "Unknown"
        };
    }

    private static string DetectVendorFromName(string name)
    {
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            return "NVIDIA";
        }

        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            return "Intel";
        }

        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        {
            return "AMD";
        }

        return "Unknown";
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
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory, string pattern)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern).ToArray() : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
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

    private static string RunCommand(string fileName, string arguments, int timeoutMilliseconds)
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

            if (process is null)
            {
                return string.Empty;
            }

            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return string.Empty;
            }

            return process.StandardOutput.ReadToEnd();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static string Get(IReadOnlyList<string> values, int index, string fallback)
    {
        return index < values.Count && !string.IsNullOrWhiteSpace(values[index]) && values[index] != "[Not Supported]"
            ? values[index]
            : fallback;
    }

    private static double? ParseNullableDouble(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static ulong? ParseNullableUlong(string? value)
    {
        return ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}
