using Application.Abstractions.Persistence;
using Application.Abstractions.Time;
using AppLogging;
using Domain.Logs;

namespace Application.Services;

public sealed class HistoryRetentionService : IAsyncDisposable
{
    private readonly IHistoryRepository _repository;
    private readonly OperationLogService _operationLogService;
    private readonly IClock _clock;
    private readonly int _retentionDays;
    private readonly int _deleteBatchSize;
    private readonly TimeSpan _runInterval;
    private CancellationTokenSource? _cancellation;
    private Task? _runTask;

    public HistoryRetentionService(
        IHistoryRepository repository,
        OperationLogService operationLogService,
        IClock clock,
        int retentionDays,
        int deleteBatchSize = 1000,
        TimeSpan? runInterval = null)
    {
        if (retentionDays <= 0) throw new ArgumentOutOfRangeException(nameof(retentionDays));
        if (deleteBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(deleteBatchSize));
        _repository = repository;
        _operationLogService = operationLogService;
        _clock = clock;
        _retentionDays = retentionDays;
        _deleteBatchSize = deleteBatchSize;
        _runInterval = runInterval ?? TimeSpan.FromDays(1);
    }

    public void Start()
    {
        if (_runTask is not null) return;
        _cancellation = new CancellationTokenSource();
        _runTask = RunAsync(_cancellation.Token);
    }

    public async Task<HistoryRetentionResult> CleanupAsync(CancellationToken cancellationToken)
    {
        var cutoffUtc = _clock.UtcNow.AddDays(-_retentionDays);
        long total = 0;
        while (true)
        {
            var deleted = await _repository.DeleteBeforeAsync(
                cutoffUtc,
                _deleteBatchSize,
                cancellationToken).ConfigureAwait(false);
            total += deleted;
            if (deleted < _deleteBatchSize) break;
            await Task.Yield();
        }

        await _operationLogService.WriteAsync(
            OperationLogLevel.Info,
            "History",
            "History.RetentionCleanup",
            nameof(HistoryRetentionService),
            "History retention cleanup completed.",
            $"CutoffUtc={cutoffUtc:O}; Deleted={total}; RetentionDays={_retentionDays}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new HistoryRetentionResult(total, cutoffUtc);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cancellation is null || _runTask is null) return;
        _cancellation.Cancel();
        try { await _runTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cancellation.Dispose();
        _cancellation = null;
        _runTask = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                AppLogger.Error(exception, "History retention cleanup failed.");
            }

            await Task.Delay(_runInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
