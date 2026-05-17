namespace LinuxMintSystemMonitor;

internal sealed class MetricsRefreshService
{
    private readonly LinuxMetricsReader _metricsReader = new();
    private readonly ProcessMetricsReader _processReader = new();

    public Task<SystemMetrics> ReadSystemMetricsAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _metricsReader.Read();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ProcessRow>> ReadProcessesAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _processReader.Read();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<StartupApplication>> ReadStartupApplicationsAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = StartupApplicationsReader.Read();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }, cancellationToken);
    }

    public Task DisableStartupAsync(StartupApplication app, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartupApplicationsReader.Disable(app);
        }, cancellationToken);
    }

    public Task EnableStartupAsync(StartupApplication app, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartupApplicationsReader.Enable(app);
        }, cancellationToken);
    }
}
