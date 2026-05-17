using Avalonia.Controls;
using Avalonia.Threading;
using System.IO.Pipes;
using System.Text;

namespace LinuxMintSystemMonitor;

internal sealed class SingleInstanceFocusService : IDisposable
{
    private const string PipeName = "linux-mint-system-monitor-focus";
    private readonly MainWindow _window;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listener;

    public SingleInstanceFocusService(MainWindow window)
    {
        _window = window;
    }

    public void Start()
    {
        _listener ??= Task.Run(ListenAsync);
    }

    public static void SignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(250);
            var bytes = Encoding.UTF8.GetBytes("activate");
            client.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
        }
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_cancellation.Token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (_window.WindowState == WindowState.Minimized)
                    {
                        _window.WindowState = WindowState.Normal;
                    }

                    _window.Activate();
                    _window.Focus();
                });
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(250, _cancellation.Token);
            }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
