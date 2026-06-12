using Application.Abstractions.Persistence;
using Application.BackgroundWorkers;
using Application.Queues;
using AppLogging;
using Domain.Alarms;
using Domain.Tags;

namespace MultiChannelMonitor.Tests.ApplicationTests;

[Collection("AppLogger serial")]
public sealed class PersistWorkerFailurePolicyTests
{
    [Fact]
    public async Task HistoryWorker_WriteFailure_LogsRetainsBatchAndRetries()
    {
        var logger = new RecordingAppLogger();
        AppLogger.Configure(logger);
        try
        {
            var repository = new FlakyHistoryRepository(failureCount: 1);
            var queue = new HistorySampleQueue();
            var worker = new HistoryPersistWorker(
                queue,
                repository,
                TimeSpan.FromMilliseconds(30),
                maxBatchSize: 1);
            using var cancellation = new CancellationTokenSource();
            var workerTask = worker.RunAsync(cancellation.Token);

            await queue.EnqueueAsync(CreateSample(1), CancellationToken.None);
            var persisted = await repository.Persisted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.False(workerTask.IsCompleted);
            Assert.Equal(2, repository.AttemptCount);
            Assert.Equal(1, Assert.Single(persisted).SequenceNo);
            var error = Assert.Single(logger.Errors);
            Assert.Equal("{0} persistence failed | Trigger: {1} | BatchSize: {2}", error.MessageTemplate);
            Assert.Equal("History", error.PropertyValues[0]);
            Assert.Equal("BatchSize", error.PropertyValues[1]);
            Assert.Equal(1, error.PropertyValues[2]);

            cancellation.Cancel();
            await workerTask;
        }
        finally
        {
            AppLogger.Reset();
        }
    }

    [Fact]
    public async Task AlarmWorker_WriteFailure_LogsRetainsBatchAndRetries()
    {
        var logger = new RecordingAppLogger();
        AppLogger.Configure(logger);
        try
        {
            var repository = new FlakyAlarmRepository(failureCount: 1);
            var queue = new AlarmEventQueue();
            var worker = new AlarmPersistWorker(
                queue,
                repository,
                TimeSpan.FromMilliseconds(30),
                maxBatchSize: 1);
            using var cancellation = new CancellationTokenSource();
            var workerTask = worker.RunAsync(cancellation.Token);

            await queue.EnqueueAsync(CreateAlarm(), CancellationToken.None);
            var persisted = await repository.Persisted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.False(workerTask.IsCompleted);
            Assert.Equal(2, repository.AttemptCount);
            Assert.Single(persisted);
            var error = Assert.Single(logger.Errors);
            Assert.Equal("{0} persistence failed | Trigger: {1} | BatchSize: {2}", error.MessageTemplate);
            Assert.Equal("Alarm", error.PropertyValues[0]);
            Assert.Equal("BatchSize", error.PropertyValues[1]);
            Assert.Equal(1, error.PropertyValues[2]);

            cancellation.Cancel();
            await workerTask;
        }
        finally
        {
            AppLogger.Reset();
        }
    }

    [Fact]
    public async Task PersistWorker_ShutdownWriteFailure_IsLoggedAndDoesNotFaultStop()
    {
        var logger = new RecordingAppLogger();
        AppLogger.Configure(logger);
        try
        {
            var repository = new FlakyHistoryRepository(failureCount: int.MaxValue);
            var queue = new HistorySampleQueue();
            var worker = new HistoryPersistWorker(
                queue,
                repository,
                TimeSpan.FromSeconds(30),
                maxBatchSize: 100);
            using var cancellation = new CancellationTokenSource();
            var workerTask = worker.RunAsync(cancellation.Token);

            await queue.EnqueueAsync(CreateSample(1), CancellationToken.None);
            cancellation.Cancel();

            await workerTask;

            var error = Assert.Single(logger.Errors);
            Assert.Equal("History", error.PropertyValues[0]);
            Assert.Equal("Shutdown", error.PropertyValues[1]);
            Assert.Equal(1, error.PropertyValues[2]);
        }
        finally
        {
            AppLogger.Reset();
        }
    }

    private static TagValue CreateSample(long sequenceNo) =>
        new(
            "TEST.TAG",
            sequenceNo,
            DateTime.UtcNow,
            TagQuality.Good,
            TagAlarmState.Normal,
            "test",
            sequenceNo);

    private static AlarmEvent CreateAlarm() =>
        new(
            Guid.NewGuid(),
            "TEST.TAG",
            AlarmLevel.Warning,
            AlarmState.Active,
            12.5,
            DateTime.UtcNow,
            "Test alarm");

    private sealed class FlakyHistoryRepository(int failureCount) : IHistoryRepository
    {
        private int _attemptCount;

        public int AttemptCount => Volatile.Read(ref _attemptCount);
        public TaskCompletionSource<IReadOnlyList<TagValue>> Persisted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task AppendAsync(IReadOnlyCollection<TagValue> samples, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _attemptCount) <= failureCount)
            {
                throw new InvalidOperationException("Expected history persistence failure.");
            }

            Persisted.TrySetResult(samples.ToArray());
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TagValue>> QueryAsync(
            string tagId,
            DateTime startTime,
            DateTime endTime,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TagValue>>([]);
    }

    private sealed class FlakyAlarmRepository(int failureCount) : IAlarmRepository
    {
        private int _attemptCount;

        public int AttemptCount => Volatile.Read(ref _attemptCount);
        public TaskCompletionSource<IReadOnlyList<AlarmEvent>> Persisted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task AppendAsync(IReadOnlyCollection<AlarmEvent> alarms, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _attemptCount) <= failureCount)
            {
                throw new InvalidOperationException("Expected alarm persistence failure.");
            }

            Persisted.TrySetResult(alarms.ToArray());
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AlarmEvent>> QueryLatestAsync(int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AlarmEvent>>([]);
    }

    private sealed class RecordingAppLogger : IAppLogger
    {
        private readonly List<ErrorEntry> _errors = [];

        public IReadOnlyList<ErrorEntry> Errors
        {
            get
            {
                lock (_errors)
                {
                    return _errors.ToArray();
                }
            }
        }

        public void Trace(string messageTemplate, params object?[] propertyValues) { }
        public void Debug(string messageTemplate, params object?[] propertyValues) { }
        public void Info(string messageTemplate, params object?[] propertyValues) { }
        public void Warn(string messageTemplate, params object?[] propertyValues) { }
        public void Error(string messageTemplate, params object?[] propertyValues) { }

        public void Error(
            Exception exception,
            string messageTemplate,
            params object?[] propertyValues)
        {
            lock (_errors)
            {
                _errors.Add(new ErrorEntry(exception, messageTemplate, propertyValues));
            }
        }

        public void Fatal(Exception exception, string messageTemplate, params object?[] propertyValues) { }
    }

    private sealed record ErrorEntry(
        Exception Exception,
        string MessageTemplate,
        object?[] PropertyValues);
}
