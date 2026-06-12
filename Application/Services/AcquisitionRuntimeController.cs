using Domain.Logs;
using AppLogging;

namespace Application.Services;

public sealed class AcquisitionRuntimeController
{
    private readonly RuntimeLifecycleCoordinator _runtimeLifecycle;
    private readonly PersistenceRuntimeCoordinator _persistenceRuntime;
    private readonly OperationLogService _operationLogService;

    public AcquisitionRuntimeController(
        RuntimeLifecycleCoordinator runtimeLifecycle,
        PersistenceRuntimeCoordinator persistenceRuntime,
        OperationLogService operationLogService)
    {
        _runtimeLifecycle = runtimeLifecycle;
        _persistenceRuntime = persistenceRuntime;
        _operationLogService = operationLogService;
        _runtimeLifecycle.StatusChanged += OnRuntimeStatusChanged;
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (!await _runtimeLifecycle.StartAsync().ConfigureAwait(false))
        {
            return false;
        }

        await _operationLogService.WriteAsync(
            OperationLogLevel.Info,
            "Acquisition",
            "Acquisition.Started",
            nameof(AcquisitionRuntimeController),
            "Acquisition started.",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        if (!await _runtimeLifecycle.StopAsync().ConfigureAwait(false))
        {
            return false;
        }

        string? flushError = null;
        try
        {
            await _persistenceRuntime.FlushHistoryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            flushError = exception.Message;
            AppLogger.Error(exception, "History flush failed after acquisition stopped.");
            await _operationLogService.WriteAsync(
                OperationLogLevel.Error,
                "Persistence",
                "History.FlushFailed",
                nameof(AcquisitionRuntimeController),
                "History flush failed after acquisition stopped.",
                exception.Message,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await _operationLogService.WriteAsync(
            OperationLogLevel.Info,
            "Acquisition",
            "Acquisition.Stopped",
            nameof(AcquisitionRuntimeController),
            "Acquisition stopped.",
            flushError is null ? null : $"HistoryFlushError={flushError}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async void OnRuntimeStatusChanged(object? sender, RuntimeLifecycleStatus status)
    {
        if (status.State != RuntimeLifecycleState.Faulted || status.Error is null)
        {
            return;
        }

        try
        {
            await _operationLogService.WriteAsync(
                OperationLogLevel.Error,
                "Acquisition",
                "Acquisition.Faulted",
                nameof(AcquisitionRuntimeController),
                "Acquisition stopped unexpectedly.",
                status.Error.Message).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Failed to enqueue acquisition fault operation log.");
        }
    }
}
