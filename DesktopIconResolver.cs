namespace LinuxMintSystemMonitor;

internal sealed class DesktopIconResolver
{
    private readonly Lazy<IReadOnlyList<DesktopIconEntry>> _desktopEntries = new(LoadDesktopEntries);
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string? Resolve(string displayName, string? executablePath, string? appGroupKey, string? appGroupName)
    {
        var executableName = string.IsNullOrWhiteSpace(executablePath)
            ? displayName.Trim('[', ']')
            : Path.GetFileName(executablePath);
        var cacheKey = $"{displayName}|{executablePath}|{appGroupKey}|{appGroupName}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var candidates = BuildCandidates(displayName, executableName, appGroupKey, appGroupName);
        var iconName = FindDesktopIcon(candidates, executableName);
        iconName ??= FindCommonIcon(candidates);
        var path = ResolveIconPath(iconName);
        _cache[cacheKey] = path;
        return path;
    }

    private string? FindDesktopIcon(IReadOnlySet<string> candidates, string executableName)
    {
        foreach (var entry in _desktopEntries.Value)
        {
            if (candidates.Contains(entry.Id)
                || candidates.Contains(entry.Name)
                || candidates.Contains(entry.ExecName)
                || string.Equals(entry.ExecName, executableName, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Icon;
            }
        }

        return null;
    }

    private static string? FindCommonIcon(IReadOnlySet<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Contains("chrome", StringComparison.OrdinalIgnoreCase))
            {
                return "google-chrome";
            }

            if (candidate.Contains("chromium", StringComparison.OrdinalIgnoreCase))
            {
                return "chromium";
            }

            if (candidate is "code" or "vscode" || candidate.Contains("visual studio code", StringComparison.OrdinalIgnoreCase))
            {
                return "visual-studio-code";
            }

            if (candidate.Contains("terminal", StringComparison.OrdinalIgnoreCase))
            {
                return "utilities-terminal";
            }

            if (candidate.Contains("nemo", StringComparison.OrdinalIgnoreCase))
            {
                return "nemo";
            }
        }

        return null;
    }

    private static HashSet<string> BuildCandidates(string displayName, string executableName, string? appGroupKey, string? appGroupName)
    {
        var values = new[] { displayName, executableName, appGroupKey, appGroupName }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(static value =>
            {
                var text = value!;
                return new[]
                {
                    text,
                    Path.GetFileNameWithoutExtension(text),
                    text.Replace(" ", string.Empty, StringComparison.Ordinal),
                    text.Replace("-", string.Empty, StringComparison.Ordinal)
                };
            });

        return values
            .Select(static value => value.Trim('[', ']').ToLowerInvariant())
            .Where(static value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<DesktopIconEntry> LoadDesktopEntries()
    {
        var directories = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications"),
            "/usr/share/applications",
            "/usr/local/share/applications",
            "/var/lib/flatpak/exports/share/applications",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "flatpak", "exports", "share", "applications"),
            "/var/lib/snapd/desktop/applications"
        };

        var entries = new List<DesktopIconEntry>();
        foreach (var directory in directories)
        {
            foreach (var path in EnumerateDesktopFiles(directory))
            {
                var values = ReadDesktopValues(path);
                values.TryGetValue("Name", out var name);
                values.TryGetValue("Exec", out var exec);
                values.TryGetValue("Icon", out var icon);
                if (string.IsNullOrWhiteSpace(icon))
                {
                    continue;
                }

                var execName = ExtractExecName(exec);
                var id = Path.GetFileNameWithoutExtension(path);
                entries.Add(new DesktopIconEntry(id, name ?? id, execName, icon));
            }
        }

        return entries;
    }

    private static IEnumerable<string> EnumerateDesktopFiles(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.desktop").ToArray()
                : [];
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

    private static Dictionary<string, string> ReadDesktopValues(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var inDesktopEntry = false;
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    inDesktopEntry = trimmed == "[Desktop Entry]";
                    continue;
                }

                if (!inDesktopEntry)
                {
                    continue;
                }

                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2)
                {
                    values[parts[0]] = parts[1];
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

    private static string ExtractExecName(string? exec)
    {
        if (string.IsNullOrWhiteSpace(exec))
        {
            return string.Empty;
        }

        var command = exec.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(static part => !part.StartsWith('%')) ?? string.Empty;
        return Path.GetFileName(command).ToLowerInvariant();
    }

    private static string? ResolveIconPath(string? iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return null;
        }

        if (Path.IsPathRooted(iconName))
        {
            return File.Exists(iconName) ? iconName : null;
        }

        var names = new[]
        {
            iconName,
            iconName + ".png"
        };
        var directories = new[]
        {
            "/usr/share/icons/hicolor/64x64/apps",
            "/usr/share/icons/hicolor/48x48/apps",
            "/usr/share/icons/hicolor/32x32/apps",
            "/usr/share/icons/hicolor/24x24/apps",
            "/usr/share/icons/hicolor/16x16/apps",
            "/usr/share/pixmaps",
            "/usr/share/icons/Mint-Y/apps/64",
            "/usr/share/icons/Mint-Y/apps/48",
            "/usr/share/icons/Mint-Y/apps/32",
            "/usr/share/icons/Mint-L/apps/48"
        };

        foreach (var directory in directories)
        {
            foreach (var name in names)
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private sealed record DesktopIconEntry(string Id, string Name, string ExecName, string Icon);
}
