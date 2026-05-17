using Avalonia;
using System.ComponentModel;

namespace LinuxMintSystemMonitor;

internal static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "LinuxMintSystemMonitor.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            SingleInstanceFocusService.SignalExistingInstance();
            return;
        }

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex) when (IsDisplayStartupFailure(ex))
        {
            Console.Error.WriteLine("Linux Mint System Monitor could not open a graphical display.");
            Console.Error.WriteLine("Start it from a desktop session with X11 or Wayland available.");
            Console.Error.WriteLine(ex.Message);
            Environment.ExitCode = 1;
        }
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }

    private static bool IsDisplayStartupFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception)
            {
                return true;
            }

            if (current.Message.Contains("XOpenDisplay", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("Wayland", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("display", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
