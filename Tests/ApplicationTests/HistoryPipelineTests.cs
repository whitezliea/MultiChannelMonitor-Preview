using System.Runtime.CompilerServices;
using Application.Abstractions.DataSource;
using Application.Abstractions.Events;
using Application.Abstractions.Persistence;
using Application.BackgroundWorkers;
using Application.Events;
using Application.Queues;
using Application.Services;
using Domain.Measurements;
using Domain.Tags;
using Simulator.Generators;
using Tests.Support;

namespace Tests.ApplicationTests;

public class HistoryPipelineTests
{
    [Fact]
    public async Task RuntimeStateConsumer_MapsOnlyHistorizedNumericStates()
    {
        var repository = new RecordingHistoryRepository();
        var queue = new HistorySampleQueue();
        var historyService = new HistoryService(repository, queue);
        var consumer = new HistoryRuntimeStateConsumer(
            historyService,
            [
                CreateDefinition("HISTORY.NUMERIC", isHistorized: true),
                CreateDefinition("NO.HISTORY", isHistorized: false),
                CreateDefinition("HISTORY.TEXT", isHistorized: true, TagDataType.String)
            ]);
        var timestamp = DateTimeOffset.UtcNow;
        var frameId = Guid.NewGuid();
        var applicationEvent = new TagRuntimeStatesProducedEvent(
            frameId,
            7,
            timestamp.UtcDateTime,
            [
                CreateState("HISTORY.NUMERIC", 12.5, null, timestamp, frameId, 7),
                CreateState("NO.HISTORY", 99, null, timestamp, frameId, 7),
                CreateState("HISTORY.TEXT", null, "Running", timestamp, frameId, 7)
            ]);

        await consumer.HandleAsync(applicationEvent, CancellationToken.None);

        var sample = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal("HISTORY.NUMERIC", sample.TagId);
        Assert.Equal(12.5, sample.Value);
        Assert.Equal(timestamp.UtcDateTime, sample.Timestamp);
        Assert.Equal(frameId.ToString("D"), sample.Source);
        Assert.Equal(7, sample.SequenceNo);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public async Task PersistWorker_FlushesWhenBatchIntervalElapses()
    {
        var repository = new RecordingHistoryRepository();
        var queue = new HistorySampleQueue();
        var worker = new HistoryPersistWorker(
            queue,
            repository,
            TimeSpan.FromMilliseconds(40),
            maxBatchSize: 100);
        using var cancellation = new CancellationTokenSource();
        var workerTask = worker.RunAsync(cancellation.Token);

        await queue.EnqueueAsync(CreateSample(1), CancellationToken.None);
        var batch = await repository.WaitForBatchAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        await workerTask;
        Assert.Single(batch);
        Assert.Equal(1, batch[0].SequenceNo);
    }

    [Fact]
    public async Task PersistWorker_FlushesImmediatelyWhenBatchIsFull()
    {
        var repository = new RecordingHistoryRepository();
        var queue = new HistorySampleQueue();
        var worker = new HistoryPersistWorker(
            queue,
            repository,
            TimeSpan.FromSeconds(30),
            maxBatchSize: 2);
        using var cancellation = new CancellationTokenSource();
        var workerTask = worker.RunAsync(cancellation.Token);

        await queue.EnqueueAsync(CreateSample(1), CancellationToken.None);
        await queue.EnqueueAsync(CreateSample(2), CancellationToken.None);
        var batch = await repository.WaitForBatchAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        await workerTask;
        Assert.Equal(2, batch.Count);
        Assert.Equal([1L, 2L], batch.Select(sample => sample.SequenceNo));
    }

    [Fact]
    public async Task PersistWorker_FlushesRemainingSamplesWhenStopped()
    {
        var repository = new RecordingHistoryRepository();
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

        var samples = repository.Snapshot();
        var sample = Assert.Single(samples);
        Assert.Equal(1, sample.SequenceNo);
    }

    [Fact]
    public async Task PersistenceRuntime_FlushesHistoryAfterAcquisitionEnds()
    {
        var definitions = TagDefinitionCatalog.CreateDefaults();
        var repository = new RecordingHistoryRepository();
        var queue = new HistorySampleQueue();
        var historyService = new HistoryService(repository, queue);
        var worker = new HistoryPersistWorker(
            queue,
            repository,
            TimeSpan.FromSeconds(30),
            maxBatchSize: 100);
        var publisher = new ApplicationEventPublisher();
        publisher.Register(new HistoryRuntimeStateConsumer(historyService, definitions));
        var frame = new FakeDataGenerator().NextFrame(DateTime.UtcNow);
        var clock = new TestClock(frame.Timestamp.AddSeconds(1));
        var runtime = new MonitoringRuntimeService(
            new DataSourceService(new FixedDataSource(frame)),
            new Application.Pipelines.DataCleanPipeline(definitions),
            new AlarmService(definitions),
            publisher,
            clock);
        await using var persistenceRuntime = new PersistenceRuntimeCoordinator(worker);

        Assert.True(await persistenceRuntime.StartAsync());
        await runtime.RunAsync(CancellationToken.None);
        await persistenceRuntime.FlushHistoryAsync();

        var samples = repository.Snapshot();
        Assert.NotEmpty(samples);
        Assert.All(samples, sample => Assert.Equal(frame.SequenceNo, sample.SequenceNo));
        Assert.DoesNotContain(samples, sample => sample.TagId == "DEVICE.SEQUENCE_NO");
        Assert.DoesNotContain(samples, sample => sample.TagId == "MATRIX.LIGHT.HOTSPOT_ROW");
    }

    private static TagDefinition CreateDefinition(
        string tagId,
        bool isHistorized,
        TagDataType dataType = TagDataType.Double) =>
        new(
            tagId,
            tagId,
            TagCategory.Measurement,
            "u",
            IsHistorized: isHistorized,
            DataType: dataType);

    private static TagRuntimeState CreateState(
        string tagId,
        double? numericValue,
        string? textValue,
        DateTimeOffset timestamp,
        Guid frameId,
        long sequenceNo) =>
        new(
            tagId,
            tagId,
            TagCategory.Measurement,
            numericValue,
            textValue,
            null,
            "u",
            numericValue.HasValue ? TagDataType.Double : TagDataType.String,
            TagQuality.Good,
            TagAlarmState.Normal,
            timestamp,
            frameId,
            sequenceNo,
            timestamp);

    private static TagValue CreateSample(long sequenceNo) =>
        new(
            "TEST.TAG",
            sequenceNo,
            DateTime.UtcNow.AddMilliseconds(sequenceNo),
            TagQuality.Good,
            TagAlarmState.Normal,
            "test",
            sequenceNo);

    private sealed class RecordingHistoryRepository : IHistoryRepository
    {
        private readonly List<TagValue> _samples = [];
        private readonly object _syncRoot = new();
        private readonly TaskCompletionSource<IReadOnlyList<TagValue>> _firstBatch =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task AppendAsync(IReadOnlyCollection<TagValue> samples, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = samples.ToArray();
            lock (_syncRoot)
            {
                _samples.AddRange(batch);
            }

            _firstBatch.TrySetResult(batch);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TagValue>> QueryAsync(
            string tagId,
            DateTime startTime,
            DateTime endTime,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TagValue>>(
                Snapshot()
                    .Where(sample => sample.TagId == tagId
                        && sample.Timestamp >= startTime
                        && sample.Timestamp <= endTime)
                    .ToArray());

        public IReadOnlyList<TagValue> Snapshot()
        {
            lock (_syncRoot)
            {
                return _samples.ToArray();
            }
        }

        public async Task<IReadOnlyList<TagValue>> WaitForBatchAsync(TimeSpan timeout) =>
            await _firstBatch.Task.WaitAsync(timeout);
    }

    private sealed class FixedDataSource(params RawMeasurementFrame[] frames) : IDataSource
    {
        public async IAsyncEnumerable<RawMeasurementFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return frame;
                await Task.Yield();
            }
        }
    }
}
