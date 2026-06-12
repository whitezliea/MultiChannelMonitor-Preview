using System.Threading.Channels;
using AppLogging;

namespace Application.BackgroundWorkers;

public enum PersistWorkerState
{
    Stopped,
    Running,
    Degraded,
    Faulted
}

public sealed record PersistWorkerStatus(
    PersistWorkerState State,
    DateTime? LastSuccessfulFlushUtc = null,
    Exception? LastError = null);

public interface IPersistWorker
{
    string Name { get; }
    PersistWorkerStatus Status { get; }
    event EventHandler<PersistWorkerStatus>? StatusChanged;
    Task RunAsync(CancellationToken cancellationToken);
    Task FlushAsync(CancellationToken cancellationToken = default);
}

public abstract class BatchPersistWorker<T> : IPersistWorker
{
    private readonly Channel<TaskCompletionSource> _flushRequests =
        Channel.CreateUnbounded<TaskCompletionSource>();
    private readonly object _lifecycleLock = new();
    private PersistWorkerStatus _status = new(PersistWorkerState.Stopped);
    private bool _running;

    protected BatchPersistWorker(TimeSpan batchInterval, int maxBatchSize)
    {
        if (batchInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(batchInterval));
        }

        if (maxBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBatchSize));
        }

        BatchInterval = batchInterval;
        MaxBatchSize = maxBatchSize;
    }

    public abstract string Name { get; }
    public TimeSpan BatchInterval { get; }
    protected int MaxBatchSize { get; }
    public PersistWorkerStatus Status => Volatile.Read(ref _status);
    public event EventHandler<PersistWorkerStatus>? StatusChanged;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            if (_running)
            {
                throw new InvalidOperationException($"{Name} is already running.");
            }

            _running = true;
        }

        SetStatus(new PersistWorkerStatus(PersistWorkerState.Running));
        var buffer = new List<T>();
        using var timer = new PeriodicTimer(BatchInterval);
        Task<T>? pendingRead = null;
        Task<bool>? pendingTick = null;
        Task<TaskCompletionSource>? pendingFlushRequest = null;

        try
        {
            while (true)
            {
                pendingRead ??= DequeueAsync(cancellationToken).AsTask();
                pendingTick ??= timer.WaitForNextTickAsync(cancellationToken).AsTask();
                pendingFlushRequest ??= _flushRequests.Reader.ReadAsync(cancellationToken).AsTask();

                var completedTask = await Task.WhenAny(
                    pendingRead,
                    pendingTick,
                    pendingFlushRequest).ConfigureAwait(false);

                if (completedTask == pendingRead)
                {
                    buffer.Add(await pendingRead.ConfigureAwait(false));
                    pendingRead = null;
                    DrainQueue(buffer);
                    if (buffer.Count >= MaxBatchSize)
                    {
                        await TryFlushAsync(buffer, "BatchSize", cancellationToken).ConfigureAwait(false);
                    }
                }

                if (completedTask == pendingTick)
                {
                    if (!await pendingTick.ConfigureAwait(false))
                    {
                        break;
                    }

                    pendingTick = null;
                    await TryFlushAsync(buffer, "Interval", cancellationToken).ConfigureAwait(false);
                }

                if (completedTask == pendingFlushRequest)
                {
                    var request = await pendingFlushRequest.ConfigureAwait(false);
                    pendingFlushRequest = null;
                    CaptureCompletedRead(ref pendingRead, buffer);
                    DrainQueue(buffer);
                    try
                    {
                        var succeeded = await TryFlushAsync(
                            buffer,
                            "Explicit",
                            cancellationToken).ConfigureAwait(false);
                        if (!succeeded)
                        {
                            throw new InvalidOperationException($"{Name} explicit flush failed.", Status.LastError);
                        }

                        request.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        request.TrySetException(exception);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetStatus(new PersistWorkerStatus(
                PersistWorkerState.Faulted,
                Status.LastSuccessfulFlushUtc,
                exception));
            throw;
        }
        finally
        {
            if (pendingFlushRequest is { IsCompletedSuccessfully: true })
            {
                pendingFlushRequest.Result.TrySetException(
                    new InvalidOperationException($"{Name} stopped before the flush completed."));
            }

            if (pendingRead is not null
                && (pendingRead.IsCompleted || cancellationToken.IsCancellationRequested))
            {
                try
                {
                    buffer.Add(await pendingRead.ConfigureAwait(false));
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    AppLogger.Error(exception, "{0} pending queue read failed during shutdown.", Name);
                }
            }

            DrainQueue(buffer);
            await TryFlushAsync(buffer, "Shutdown", CancellationToken.None).ConfigureAwait(false);
            while (_flushRequests.Reader.TryRead(out var request))
            {
                request.TrySetException(new InvalidOperationException($"{Name} has stopped."));
            }

            lock (_lifecycleLock)
            {
                _running = false;
            }

            if (Status.State != PersistWorkerState.Faulted && Status.LastError is null)
            {
                SetStatus(new PersistWorkerStatus(
                    PersistWorkerState.Stopped,
                    Status.LastSuccessfulFlushUtc));
            }
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleLock)
        {
            if (!_running)
            {
                throw new InvalidOperationException($"{Name} is not running.");
            }
        }

        var request = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _flushRequests.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        await request.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected abstract ValueTask<T> DequeueAsync(CancellationToken cancellationToken);
    protected abstract bool TryDequeue(out T item);
    protected abstract Task PersistAsync(
        IReadOnlyCollection<T> items,
        CancellationToken cancellationToken);

    private void DrainQueue(List<T> buffer)
    {
        while (TryDequeue(out var item))
        {
            buffer.Add(item);
        }
    }

    private static void CaptureCompletedRead(ref Task<T>? pendingRead, List<T> buffer)
    {
        if (pendingRead is { IsCompletedSuccessfully: true })
        {
            buffer.Add(pendingRead.Result);
            pendingRead = null;
        }
    }

    private async Task<bool> TryFlushAsync(
        List<T> buffer,
        string trigger,
        CancellationToken cancellationToken)
    {
        if (buffer.Count == 0)
        {
            return true;
        }

        var batch = buffer.ToArray();
        try
        {
            await PersistAsync(batch, cancellationToken).ConfigureAwait(false);
            buffer.Clear();
            SetStatus(new PersistWorkerStatus(PersistWorkerState.Running, DateTime.UtcNow));
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLogger.Error(
                exception,
                "{0} persistence failed | Trigger: {1} | BatchSize: {2}",
                Name,
                trigger,
                batch.Length);
            SetStatus(new PersistWorkerStatus(
                PersistWorkerState.Degraded,
                Status.LastSuccessfulFlushUtc,
                exception));
            return false;
        }
    }

    private void SetStatus(PersistWorkerStatus status)
    {
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(this, status);
    }
}
