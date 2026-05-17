namespace LinuxMintSystemMonitor;

internal sealed record StartupApplication(
    string Id,
    string Name,
    string Source,
    bool Enabled,
    string Command,
    string Location,
    string FileName,
    string Path);

internal static class StartupApplicationsReader
{
    private static readonly string UserAutostartDirectory =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");

    public static IReadOnlyList<StartupApplication> Read()
    {
        var entries = new Dictionary<string, DesktopEntry>(StringComparer.Ordinal);
        foreach (var path in EnumerateDesktopFiles("/etc/xdg/autostart"))
        {
            var entry = TryReadDesktopEntry(path, "System");
            if (entry is not null)
            {
                entries[entry.FileName] = entry;
            }
        }

        foreach (var path in EnumerateDesktopFiles(UserAutostartDirectory))
        {
            var entry = TryReadDesktopEntry(path, "User");
            if (entry is not null)
            {
                entries[entry.FileName] = entry;
            }
        }

        return entries.Values
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static item => new StartupApplication(
                item.FileName,
                item.Name,
                item.Source,
                !item.Hidden,
                item.Exec,
                item.Path,
                item.FileName,
                item.Path))
            .ToArray();
    }

    public static void Disable(StartupApplication app)
    {
        Directory.CreateDirectory(UserAutostartDirectory);
        var userPath = System.IO.Path.Combine(UserAutostartDirectory, app.FileName);

        if (!File.Exists(userPath))
        {
            File.Copy(app.Path, userPath, overwrite: true);
        }

        SetDesktopValue(userPath, "Hidden", "true");
    }

    public static void Enable(StartupApplication app)
    {
        var userPath = System.IO.Path.Combine(UserAutostartDirectory, app.FileName);
        if (!File.Exists(userPath))
        {
            return;
        }

        var systemPath = System.IO.Path.Combine("/etc/xdg/autostart", app.FileName);
        if (File.Exists(systemPath) && IsDisabledUserOverride(userPath))
        {
            File.Delete(userPath);
            return;
        }

        RemoveDesktopKey(userPath, "Hidden");
        SetDesktopValue(userPath, "X-GNOME-Autostart-enabled", "true");
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

    private static DesktopEntry? TryReadDesktopEntry(string path, string source)
    {
        try
        {
            var values = ReadDesktopValues(path);
            if (!values.TryGetValue("Name", out var name) || string.IsNullOrWhiteSpace(name))
            {
                name = System.IO.Path.GetFileNameWithoutExtension(path);
            }

            values.TryGetValue("Exec", out var exec);
            values.TryGetValue("Hidden", out var hidden);
            values.TryGetValue("X-GNOME-Autostart-enabled", out var gnomeEnabled);
            var isHidden = string.Equals(hidden, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(gnomeEnabled, "false", StringComparison.OrdinalIgnoreCase);

            return new DesktopEntry(
                System.IO.Path.GetFileName(path),
                name,
                source,
                isHidden,
                exec ?? string.Empty,
                path);
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

    private static Dictionary<string, string> ReadDesktopValues(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
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

        return values;
    }

    private static void SetDesktopValue(string path, string key, string value)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : ["[Desktop Entry]"];
        var inDesktopEntry = false;
        var inserted = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (inDesktopEntry && !inserted)
                {
                    lines.Insert(i, $"{key}={value}");
                    inserted = true;
                    break;
                }

                inDesktopEntry = trimmed == "[Desktop Entry]";
                continue;
            }

            if (inDesktopEntry && trimmed.StartsWith($"{key}=", StringComparison.Ordinal))
            {
                lines[i] = $"{key}={value}";
                inserted = true;
                break;
            }
        }

        if (!inserted)
        {
            lines.Add($"{key}={value}");
        }

        File.WriteAllLines(path, lines);
    }

    private static void RemoveDesktopKey(string path, string key)
    {
        var lines = File.ReadAllLines(path)
            .Where(line => !line.Trim().StartsWith($"{key}=", StringComparison.Ordinal))
            .ToArray();
        File.WriteAllLines(path, lines);
    }

    private static bool IsDisabledUserOverride(string path)
    {
        var values = ReadDesktopValues(path);
        return values.TryGetValue("Hidden", out var hidden)
            && string.Equals(hidden, "true", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DesktopEntry(
        string FileName,
        string Name,
        string Source,
        bool Hidden,
        string Exec,
        string Path);
}
