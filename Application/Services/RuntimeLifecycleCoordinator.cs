namespace Application.Services;

public enum RuntimeLifecycleState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

public sealed record RuntimeLifecycleStatus(
    RuntimeLifecycleState State,
    Exception? Error = null);

public sealed class RuntimeLifecycleCoordinator : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task> _runAsync;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _runtimeCancellation;
    private Task? _runtimeTask;
    private RuntimeLifecycleStatus _status = new(RuntimeLifecycleState.Stopped);
    private bool _disposed;

    public RuntimeLifecycleCoordinator(Func<CancellationToken, Task> runAsync)
    {
        _runAsync = runAsync ?? throw new ArgumentNullException(nameof(runAsync));
    }

    public event EventHandler<RuntimeLifecycleStatus>? StatusChanged;

    public RuntimeLifecycleStatus Status => Volatile.Read(ref _status);

    public bool IsActive => Status.State is
        RuntimeLifecycleState.Starting or
        RuntimeLifecycleState.Running or
        RuntimeLifecycleState.Stopping;

    public async Task<bool> StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runtimeTask is { IsCompleted: false })
            {
                return false;
            }

            _runtimeCancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            _runtimeCancellation = cancellation;
            SetStatus(new RuntimeLifecycleStatus(RuntimeLifecycleState.Starting));
            _runtimeTask = RunRuntimeAsync(cancellation);
            SetStatus(new RuntimeLifecycleStatus(RuntimeLifecycleState.Running));
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> StopAsync()
    {
        Task? runtimeTask;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            runtimeTask = _runtimeTask;
            if (runtimeTask is null || runtimeTask.IsCompleted)
            {
                return false;
            }

            SetStatus(new RuntimeLifecycleStatus(RuntimeLifecycleState.Stopping));
            _runtimeCancellation?.Cancel();
        }
        finally
        {
            _gate.Release();
        }

        await runtimeTask.ConfigureAwait(false);
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

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _runtimeCancellation?.Dispose();
            _runtimeCancellation = null;
            _runtimeTask = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task RunRuntimeAsync(CancellationTokenSource cancellation)
    {
        Exception? failure = null;
        try
        {
            await _runAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_runtimeCancellation, cancellation))
                {
                    cancellation.Dispose();
                    _runtimeCancellation = null;
                    _runtimeTask = null;
                    SetStatus(failure is null
                        ? new RuntimeLifecycleStatus(RuntimeLifecycleState.Stopped)
                        : new RuntimeLifecycleStatus(RuntimeLifecycleState.Faulted, failure));
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private void SetStatus(RuntimeLifecycleStatus status)
    {
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(this, status);
    }
}
