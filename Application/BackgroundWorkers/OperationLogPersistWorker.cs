using Application.Abstractions.Persistence;
using Application.Queues;
using Domain.Logs;

namespace Application.BackgroundWorkers;

public sealed class OperationLogPersistWorker : BatchPersistWorker<OperationLog>
{
    private readonly OperationLogQueue _queue;
    private readonly IOperationLogRepository _repository;

    public OperationLogPersistWorker(
        OperationLogQueue queue,
        IOperationLogRepository repository,
        TimeSpan batchInterval,
        int maxBatchSize = 100) : base(batchInterval, maxBatchSize)
    {
        _queue = queue;
        _repository = repository;
    }

    public override string Name => "OperationLog";

    protected override ValueTask<OperationLog> DequeueAsync(CancellationToken cancellationToken) =>
        _queue.DequeueAsync(cancellationToken);

    protected override bool TryDequeue(out OperationLog item) => _queue.TryDequeue(out item);

    protected override Task PersistAsync(
        IReadOnlyCollection<OperationLog> items,
        CancellationToken cancellationToken) =>
        _repository.AppendAsync(items, cancellationToken);
}
