using Application.BackgroundWorkers;

namespace Application.Services;

public enum PersistenceRuntimeState
{
    Stopped,
    Running,
    Degraded,
    Faulted
}

public sealed record PersistenceRuntimeStatus(
    PersistenceRuntimeState State,
    string? WorkerName = null,
    Exception? Error = null);

public sealed class PersistenceRuntimeCoordinator : IAsyncDisposable
{
    private readonly IReadOnlyList<IPersistWorker> _workers;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private Task[] _workerTasks = [];
    private PersistenceRuntimeStatus _status = new(PersistenceRuntimeState.Stopped);
    private bool _disposed;

    public PersistenceRuntimeCoordinator(params IPersistWorker[] workers)
    {
        ArgumentNullException.ThrowIfNull(workers);
        if (workers.Length == 0)
        {
            throw new ArgumentException("At least one persistence worker is required.", nameof(workers));
        }

        _workers = workers;
        foreach (var worker in _workers)
        {
            worker.StatusChanged += OnWorkerStatusChanged;
        }
    }

    public PersistenceRuntimeStatus Status => Volatile.Read(ref _status);
    public bool IsRunning => Status.State is PersistenceRuntimeState.Running or PersistenceRuntimeState.Degraded;
    public event EventHandler<PersistenceRuntimeStatus>? StatusChanged;

    public async Task<bool> StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_workerTasks.Any(task => !task.IsCompleted))
            {
                return false;
            }

            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            _workerTasks = _workers
                .Select(worker => ObserveWorkerAsync(worker, _cancellation.Token))
                .ToArray();
            SetStatus(new PersistenceRuntimeStatus(PersistenceRuntimeState.Running));
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task FlushHistoryAsync(CancellationToken cancellationToken = default)
        => FlushWorkerAsync("History", cancellationToken);

    public Task FlushOperationLogsAsync(CancellationToken cancellationToken = default)
        => FlushWorkerAsync("OperationLog", cancellationToken);

    private Task FlushWorkerAsync(string workerName, CancellationToken cancellationToken)
    {
        var worker = _workers.SingleOrDefault(item => item.Name == workerName)
            ?? throw new InvalidOperationException($"{workerName} persistence worker is not configured.");
        return worker.FlushAsync(cancellationToken);
    }

    public async Task<bool> StopAsync()
    {
        Task[] workerTasks;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            workerTasks = _workerTasks;
            if (workerTasks.Length == 0 || workerTasks.All(task => task.IsCompleted))
            {
                return false;
            }

            _cancellation?.Cancel();
        }
        finally
        {
            _gate.Release();
        }

        await Task.WhenAll(workerTasks).ConfigureAwait(false);
        if (Status.State == PersistenceRuntimeState.Running)
        {
            SetStatus(new PersistenceRuntimeStatus(PersistenceRuntimeState.Stopped));
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }
        finally
        {
            _gate.Release();
        }

        await StopAsync().ConfigureAwait(false);
        foreach (var worker in _workers)
        {
            worker.StatusChanged -= OnWorkerStatusChanged;
        }

        _cancellation?.Dispose();
        _gate.Dispose();
    }

    private async Task ObserveWorkerAsync(IPersistWorker worker, CancellationToken cancellationToken)
    {
        try
        {
            await worker.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SetStatus(new PersistenceRuntimeStatus(
                PersistenceRuntimeState.Faulted,
                worker.Name,
                exception));
        }
    }

    private void OnWorkerStatusChanged(object? sender, PersistWorkerStatus status)
    {
        if (sender is not IPersistWorker worker)
        {
            return;
        }

        if (status.State == PersistWorkerState.Faulted)
        {
            SetStatus(new PersistenceRuntimeStatus(
                PersistenceRuntimeState.Faulted,
                worker.Name,
                status.LastError));
        }
        else if (status.State == PersistWorkerState.Degraded
            && Status.State != PersistenceRuntimeState.Faulted)
        {
            SetStatus(new PersistenceRuntimeStatus(
                PersistenceRuntimeState.Degraded,
                worker.Name,
                status.LastError));
        }
        else if (status.State == PersistWorkerState.Running
            && _workers.All(item => item.Status.State == PersistWorkerState.Running))
        {
            SetStatus(new PersistenceRuntimeStatus(PersistenceRuntimeState.Running));
        }
    }

    private void SetStatus(PersistenceRuntimeStatus status)
    {
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(this, status);
    }
}
