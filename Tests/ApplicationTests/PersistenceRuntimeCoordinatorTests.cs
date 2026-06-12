using Application.Abstractions.Persistence;
using Application.BackgroundWorkers;
using Application.Queues;
using Application.Services;
using Domain.Alarms;
using Domain.Logs;
using Domain.Tags;
using Infrastructure.Persistence;
using Tests.Support;

namespace Tests.ApplicationTests;

public sealed class PersistenceRuntimeCoordinatorTests
{
    [Fact]
    public async Task StartAsync_StartsWorkersOnlyOnce()
    {
        var worker = new ControllablePersistWorker("History");
        await using var coordinator = new PersistenceRuntimeCoordinator(worker);

        Assert.True(await coordinator.StartAsync());
        await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(await coordinator.StartAsync());
        Assert.Equal(PersistenceRuntimeState.Running, coordinator.Status.State);
    }

    [Fact]
    public async Task FlushHistoryAsync_PersistsPendingSamplesWithoutStoppingWorkers()
    {
        var repository = new RecordingHistoryRepository();
        var queue = new HistorySampleQueue();
        var worker = new HistoryPersistWorker(
            queue,
            repository,
            TimeSpan.FromSeconds(30),
            maxBatchSize: 100);
        await using var coordinator = new PersistenceRuntimeCoordinator(worker);
        Assert.True(await coordinator.StartAsync());
        await queue.EnqueueAsync(CreateSample(), CancellationToken.None);

        await coordinator.FlushHistoryAsync();

        Assert.Single(repository.Samples);
        Assert.True(coordinator.IsRunning);
        Assert.Equal(PersistWorkerState.Running, worker.Status.State);
    }

    [Fact]
    public async Task AlarmWorker_RemainsAvailableAfterAcquisitionStops()
    {
        var repository = new RecordingAlarmRepository();
        var queue = new AlarmEventQueue();
        var worker = new AlarmPersistWorker(
            queue,
            repository,
            TimeSpan.FromMilliseconds(30),
            maxBatchSize: 100);
        await using var coordinator = new PersistenceRuntimeCoordinator(worker);
        Assert.True(await coordinator.StartAsync());

        // The acquisition runtime is intentionally absent/stopped here.
        await queue.EnqueueAsync(CreateAlarm(), CancellationToken.None);
        var persisted = await repository.Persisted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(persisted);
        Assert.True(coordinator.IsRunning);
    }

    [Fact]
    public async Task DisposeAsync_WaitsForAllWorkersToFlushRemainingQueues()
    {
        var historyRepository = new RecordingHistoryRepository();
        var alarmRepository = new RecordingAlarmRepository();
        var operationLogRepository = new InMemoryOperationLogRepository();
        var historyQueue = new HistorySampleQueue();
        var alarmQueue = new AlarmEventQueue();
        var operationLogQueue = new OperationLogQueue();
        var coordinator = new PersistenceRuntimeCoordinator(
            new HistoryPersistWorker(historyQueue, historyRepository, TimeSpan.FromSeconds(30)),
            new AlarmPersistWorker(alarmQueue, alarmRepository, TimeSpan.FromSeconds(30)),
            new OperationLogPersistWorker(operationLogQueue, operationLogRepository, TimeSpan.FromSeconds(30)));
        Assert.True(await coordinator.StartAsync());
        await historyQueue.EnqueueAsync(CreateSample(), CancellationToken.None);
        await alarmQueue.EnqueueAsync(CreateAlarm(), CancellationToken.None);
        await operationLogQueue.EnqueueAsync(
            new OperationLog(DateTime.UtcNow, OperationLogLevel.Info, "Test", "Shutdown"),
            CancellationToken.None);

        await coordinator.DisposeAsync();

        Assert.Single(historyRepository.Samples);
        Assert.Single(await alarmRepository.QueryLatestAsync(10, CancellationToken.None));
        Assert.Single(await operationLogRepository.QueryLatestAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task OperationLogWorker_FlushesSmallBatchOnInterval()
    {
        var repository = new InMemoryOperationLogRepository();
        var queue = new OperationLogQueue();
        var worker = new OperationLogPersistWorker(
            queue,
            repository,
            TimeSpan.FromMilliseconds(30),
            maxBatchSize: 100);
        await using var coordinator = new PersistenceRuntimeCoordinator(worker);
        Assert.True(await coordinator.StartAsync());
        await queue.EnqueueAsync(
            new OperationLog(
                DateTime.UtcNow,
                OperationLogLevel.Info,
                "Test",
                "Interval",
                "Test.Interval",
                "test"),
            CancellationToken.None);

        await Task.Delay(100);

        Assert.Single(await repository.QueryLatestAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task OperationLogWorker_FlushesImmediatelyWhenBatchIsFull()
    {
        var repository = new InMemoryOperationLogRepository();
        var queue = new OperationLogQueue();
        var worker = new OperationLogPersistWorker(
            queue,
            repository,
            TimeSpan.FromSeconds(30),
            maxBatchSize: 2);
        await using var coordinator = new PersistenceRuntimeCoordinator(worker);
        Assert.True(await coordinator.StartAsync());
        var clock = new TestClock(DateTime.UtcNow);
        var service = new OperationLogService(repository, queue, clock);
        await service.WriteAsync(OperationLogLevel.Info, "Test", "One", "test", "One");
        await service.WriteAsync(OperationLogLevel.Info, "Test", "Two", "test", "Two");

        var timeout = DateTime.UtcNow.AddSeconds(2);
        while ((await repository.QueryLatestAsync(10, CancellationToken.None)).Count < 2
            && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.Equal(2, (await repository.QueryLatestAsync(10, CancellationToken.None)).Count);
    }

    private static TagValue CreateSample() =>
        new(
            "TEST.TAG",
            1,
            DateTime.UtcNow,
            TagQuality.Good,
            TagAlarmState.Normal,
            "test",
            1);

    private static AlarmEvent CreateAlarm() =>
        new(
            Guid.NewGuid(),
            "TEST.TAG",
            AlarmLevel.Warning,
            AlarmState.Acknowledged,
            1,
            DateTime.UtcNow,
            "Acknowledged after acquisition stopped",
            DateTime.UtcNow);

    private sealed class ControllablePersistWorker(string name) : IPersistWorker
    {
        public string Name { get; } = name;
        public PersistWorkerStatus Status { get; private set; } = new(PersistWorkerState.Stopped);
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<PersistWorkerStatus>? StatusChanged;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            Status = new PersistWorkerStatus(PersistWorkerState.Running);
            StatusChanged?.Invoke(this, Status);
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            Status = new PersistWorkerStatus(PersistWorkerState.Stopped);
            StatusChanged?.Invoke(this, Status);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingHistoryRepository : IHistoryRepository
    {
        public List<TagValue> Samples { get; } = [];

        public Task AppendAsync(IReadOnlyCollection<TagValue> samples, CancellationToken cancellationToken)
        {
            Samples.AddRange(samples);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TagValue>> QueryAsync(
            string tagId,
            DateTime startTime,
            DateTime endTime,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TagValue>>(Samples.ToArray());
    }

    private sealed class RecordingAlarmRepository : IAlarmRepository
    {
        private readonly List<AlarmEvent> _alarms = [];
        public TaskCompletionSource<IReadOnlyList<AlarmEvent>> Persisted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task AppendAsync(IReadOnlyCollection<AlarmEvent> alarms, CancellationToken cancellationToken)
        {
            _alarms.AddRange(alarms);
            Persisted.TrySetResult(alarms.ToArray());
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AlarmEvent>> QueryLatestAsync(int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AlarmEvent>>(_alarms.Take(count).ToArray());
    }
}
