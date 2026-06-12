using Application.Abstractions.Persistence;
using Application.Queues;
using AppLogging;
using Domain.Tags;

namespace Application.BackgroundWorkers;

public sealed class HistoryPersistWorker : BatchPersistWorker<TagValue>
{
    private readonly HistorySampleQueue _queue;
    private readonly IHistoryRepository _repository;
    public HistoryPersistWorker(
        HistorySampleQueue queue,
        IHistoryRepository repository,
        TimeSpan batchInterval,
        int maxBatchSize = 100) : base(batchInterval, maxBatchSize)
    {
        _queue = queue;
        _repository = repository;
    }

    public override string Name => "History";

    protected override ValueTask<TagValue> DequeueAsync(CancellationToken cancellationToken) =>
        _queue.DequeueAsync(cancellationToken);

    protected override bool TryDequeue(out TagValue item) => _queue.TryDequeue(out item);

    protected override Task PersistAsync(
        IReadOnlyCollection<TagValue> items,
        CancellationToken cancellationToken) =>
        _repository.AppendAsync(items, cancellationToken);
}
