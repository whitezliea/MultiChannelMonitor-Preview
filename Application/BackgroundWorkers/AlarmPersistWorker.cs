using Application.Abstractions.Persistence;
using Application.Queues;
using AppLogging;
using Domain.Alarms;

namespace Application.BackgroundWorkers;

public sealed class AlarmPersistWorker : BatchPersistWorker<AlarmEvent>
{
    private readonly AlarmEventQueue _queue;
    private readonly IAlarmRepository _repository;
    public AlarmPersistWorker(
        AlarmEventQueue queue,
        IAlarmRepository repository,
        TimeSpan batchInterval,
        int maxBatchSize = 100) : base(batchInterval, maxBatchSize)
    {
        _queue = queue;
        _repository = repository;
    }

    public override string Name => "Alarm";

    protected override ValueTask<AlarmEvent> DequeueAsync(CancellationToken cancellationToken) =>
        _queue.DequeueAsync(cancellationToken);

    protected override bool TryDequeue(out AlarmEvent item) => _queue.TryDequeue(out item);

    protected override Task PersistAsync(
        IReadOnlyCollection<AlarmEvent> items,
        CancellationToken cancellationToken) =>
        _repository.AppendAsync(items, cancellationToken);
}
