using Domain.Logs;
using AppLogging;

namespace Application.Services;

public sealed class ApplicationRuntimeHost : IAsyncDisposable
{
    private readonly PersistenceRuntimeCoordinator _persistenceRuntime;
    private readonly OperationLogService _operationLogService;
    private readonly HistoryRetentionService? _historyRetentionService;
    private bool _started;
    private bool _disposed;

    public ApplicationRuntimeHost(
        PersistenceRuntimeCoordinator persistenceRuntime,
        OperationLogService operationLogService,
        HistoryRetentionService? historyRetentionService = null)
    {
        _persistenceRuntime = persistenceRuntime;
        _operationLogService = operationLogService;
        _historyRetentionService = historyRetentionService;
        _persistenceRuntime.StatusChanged += OnPersistenceStatusChanged;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        if (!await _persistenceRuntime.StartAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException("Application persistence runtime could not be started.");
        }

        _started = true;
        _historyRetentionService?.Start();
        await _operationLogService.WriteAsync(
            OperationLogLevel.Info,
            "Application",
            "Application.Started",
            nameof(ApplicationRuntimeHost),
            "Application started.",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_started && _persistenceRuntime.IsRunning)
        {
            await _operationLogService.WriteAsync(
                OperationLogLevel.Info,
                "Application",
                "Application.Exiting",
                nameof(ApplicationRuntimeHost),
                "Application is exiting.").ConfigureAwait(false);
        }

        if (_historyRetentionService is not null)
        {
            await _historyRetentionService.DisposeAsync().ConfigureAwait(false);
        }

        await _persistenceRuntime.DisposeAsync().ConfigureAwait(false);
        _persistenceRuntime.StatusChanged -= OnPersistenceStatusChanged;
    }

    private async void OnPersistenceStatusChanged(object? sender, PersistenceRuntimeStatus status)
    {
        if (!_started
            || status.State is not (PersistenceRuntimeState.Degraded or PersistenceRuntimeState.Faulted)
            || string.Equals(status.WorkerName, "OperationLog", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await _operationLogService.WriteAsync(
                OperationLogLevel.Error,
                "Persistence",
                $"Persistence.{status.State}",
                nameof(ApplicationRuntimeHost),
                $"{status.WorkerName} persistence is {status.State}.",
                status.Error?.Message).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Failed to enqueue persistence health operation log.");
        }
    }
}
