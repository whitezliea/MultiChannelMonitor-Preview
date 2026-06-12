using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Collections.Concurrent;
using Application.Abstractions.DataSource;
using Application.Abstractions.Events;
using Application.Configuration;
using Application.Events;
using Application.Pipelines;
using Application.Services;
using Domain.Measurements;
using Domain.Tags;
using Simulator.Generators;
using Tests.Support;

namespace Tests.ApplicationTests;

public sealed class DataSourceHealthMonitorTests
{
    [Fact]
    public void TimeoutAndRecovery_AreEachProducedOnce()
    {
        var now = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        var clock = new TestClock(now);
        var timeProvider = new ManualTimeProvider();
        var monitor = new DataSourceHealthMonitor(clock, timeProvider);
        var session = monitor.StartSession();
        var frame = CreateFrame(now);
        Assert.Null(monitor.RecordFrame(session, frame));
        var snapshot = monitor.Snapshot(session);

        timeProvider.Advance(TimeSpan.FromSeconds(3));
        var timedOut = monitor.TryMarkTimedOut(snapshot, TimeSpan.FromSeconds(2));
        var repeatedTimeout = monitor.TryMarkTimedOut(snapshot, TimeSpan.FromSeconds(2));
        clock.UtcNow = now.AddSeconds(4);
        var recovered = monitor.RecordFrame(session, CreateFrame(clock.UtcNow));
        var repeatedRecovery = monitor.RecordFrame(session, CreateFrame(clock.UtcNow.AddSeconds(1)));

        Assert.NotNull(timedOut);
        Assert.Null(repeatedTimeout);
        Assert.NotNull(recovered);
        Assert.Null(repeatedRecovery);
    }

    [Fact]
    public void NewFrameInvalidatesEarlierTimeoutSnapshot()
    {
        var now = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        var monitor = new DataSourceHealthMonitor(new TestClock(now), new ManualTimeProvider());
        var session = monitor.StartSession();
        monitor.RecordFrame(session, CreateFrame(now));
        var staleSnapshot = monitor.Snapshot(session);

        monitor.RecordFrame(session, CreateFrame(now.AddSeconds(1)));

        Assert.Null(monitor.TryMarkTimedOut(staleSnapshot, TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void StopSessionInvalidatesPendingTimeout()
    {
        var now = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        var timeProvider = new ManualTimeProvider();
        var monitor = new DataSourceHealthMonitor(new TestClock(now), timeProvider);
        var session = monitor.StartSession();
        monitor.RecordFrame(session, CreateFrame(now));
        var snapshot = monitor.Snapshot(session);
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        monitor.StopSession(session);

        Assert.Null(monitor.TryMarkTimedOut(snapshot, TimeSpan.FromSeconds(1)));
        Assert.Equal(DataSourceHealthState.Stopped, monitor.Status.State);
    }

    [Fact]
    public async Task RuntimeSilence_MarksTagsOfflineOnceAndRecoversOnce()
    {
        var definitions = TagDefinitionCatalog.CreateDefaults();
        var source = new ChannelDataSource();
        var publisher = new ApplicationEventPublisher();
        var tags = new RecordingHandler<TagRuntimeStatesProducedEvent>();
        var timeouts = new RecordingHandler<DataSourceTimedOutEvent>();
        var recoveries = new RecordingHandler<DataSourceRecoveredEvent>();
        var raisedAlarms = new RecordingHandler<AlarmRaisedEvent>();
        var recoveredAlarms = new RecordingHandler<AlarmRecoveredEvent>();
        publisher.Register(tags);
        publisher.Register(timeouts);
        publisher.Register(recoveries);
        publisher.Register(raisedAlarms);
        publisher.Register(recoveredAlarms);
        var clock = new SystemClockProxy();
        var matrixService = new MeasurementMapService(new Application.Caches.MatrixFrameCache());
        var processedStore = new Application.Caches.ProcessedFrameSnapshotStore();
        var runtime = new MonitoringRuntimeService(
            new DataSourceService(source),
            new DataCleanPipeline(definitions),
            new AlarmService(definitions),
            publisher,
            clock,
            new DataSourceHealthMonitor(clock),
            new RuntimeOptionsStore(new MonitorRuntimeOptions
            {
                DataGenerateInterval = TimeSpan.FromMilliseconds(20),
                DataSourceTimeoutPeriods = 2
            }),
            processedStore,
            matrixService);
        using var cancellation = new CancellationTokenSource();
        var runTask = runtime.RunAsync(cancellation.Token);
        var generator = new FakeDataGenerator();
        await source.WriteAsync(generator.NextFrame(DateTime.UtcNow));

        await WaitUntilAsync(() => timeouts.Events.Count == 1
            && tags.Events.Any(IsOffline)
            && raisedAlarms.Events.Count(alarm => alarm.Alarm.TagId == "MEAS.TEMP.CH01") == 1,
            TimeSpan.FromSeconds(2));
        var offlineSnapshot = processedStore.GetLatest()!;
        Assert.All(offlineSnapshot.TagRuntimeStates, state => Assert.Equal(offlineSnapshot.SourceFrameId, state.SourceFrameId));
        Assert.Equal(offlineSnapshot.SourceFrameId, offlineSnapshot.MatrixAnalysis?.SourceFrameId);
        Assert.Equal(offlineSnapshot.SequenceNo, offlineSnapshot.MatrixAnalysis?.SequenceNo);
        await Task.Delay(80);
        Assert.Single(timeouts.Events);
        Assert.Single(raisedAlarms.Events, alarm => alarm.Alarm.TagId == "MEAS.TEMP.CH01");

        await source.WriteAsync(generator.NextFrame(DateTime.UtcNow));
        await WaitUntilAsync(() => recoveries.Events.Count == 1
            && tags.Events.Count >= 3
            && !IsOffline(tags.Events[^1])
            && recoveredAlarms.Events.Count(alarm => alarm.Alarm.TagId == "MEAS.TEMP.CH01") == 1,
            TimeSpan.FromSeconds(2));
        Assert.Single(recoveries.Events);
        Assert.Single(recoveredAlarms.Events, alarm => alarm.Alarm.TagId == "MEAS.TEMP.CH01");
        var recoveredSnapshot = processedStore.GetLatest()!;
        Assert.Equal(recoveredSnapshot.SourceFrameId, recoveredSnapshot.MatrixAnalysis?.SourceFrameId);
        Assert.All(recoveredSnapshot.TagRuntimeStates, state => Assert.Equal(recoveredSnapshot.SourceFrameId, state.SourceFrameId));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
    }

    [Fact]
    public async Task NewSession_FirstFrameIntervalDoesNotIncludeStoppedDuration()
    {
        var definitions = TagDefinitionCatalog.CreateDefaults();
        var start = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        var generator = new FakeDataGenerator();
        var source = new SessionFramesDataSource(
            generator.NextFrame(start),
            generator.NextFrame(start.AddHours(1)));
        var publisher = new ApplicationEventPublisher();
        var tags = new RecordingHandler<TagRuntimeStatesProducedEvent>();
        publisher.Register(tags);
        var clock = new TestClock(start.AddHours(1));
        var runtime = new MonitoringRuntimeService(
            new DataSourceService(source),
            new DataCleanPipeline(definitions),
            new AlarmService(definitions),
            publisher,
            clock);

        await runtime.RunAsync(CancellationToken.None);
        await runtime.RunAsync(CancellationToken.None);

        var intervals = tags.Events.Select(applicationEvent => applicationEvent.States
            .Single(state => state.TagId == "DEVICE.FRAME_INTERVAL_MS").NumericValue).ToArray();
        Assert.Equal([0d, 0d], intervals);
    }

    [Fact]
    public async Task UserStop_DoesNotPublishTimeoutOrOfflineState()
    {
        var definitions = TagDefinitionCatalog.CreateDefaults();
        var source = new ChannelDataSource();
        var publisher = new ApplicationEventPublisher();
        var tags = new RecordingHandler<TagRuntimeStatesProducedEvent>();
        var timeouts = new RecordingHandler<DataSourceTimedOutEvent>();
        publisher.Register(tags);
        publisher.Register(timeouts);
        var clock = new SystemClockProxy();
        var runtime = new MonitoringRuntimeService(
            new DataSourceService(source),
            new DataCleanPipeline(definitions),
            new AlarmService(definitions),
            publisher,
            clock,
            new DataSourceHealthMonitor(clock),
            new RuntimeOptionsStore(new MonitorRuntimeOptions
            {
                DataGenerateInterval = TimeSpan.FromMilliseconds(500),
                DataSourceTimeoutPeriods = 3
            }));
        using var cancellation = new CancellationTokenSource();
        var runTask = runtime.RunAsync(cancellation.Token);
        await source.WriteAsync(new FakeDataGenerator().NextFrame(DateTime.UtcNow));
        await WaitUntilAsync(() => tags.Events.Count == 1, TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        await Task.Delay(150);

        Assert.Empty(timeouts.Events);
        Assert.DoesNotContain(tags.Events, IsOffline);
    }

    private static bool IsOffline(TagRuntimeStatesProducedEvent applicationEvent) =>
        applicationEvent.States.Count > 0 && applicationEvent.States.All(state => state.Quality == TagQuality.Offline);

    private static RawMeasurementFrame CreateFrame(DateTime timestampUtc) =>
        new FakeDataGenerator().NextFrame(timestampUtc);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition(), "Expected condition was not reached before timeout.");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }

    private sealed class SystemClockProxy : Application.Abstractions.Time.IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    private sealed class ChannelDataSource : IDataSource
    {
        private readonly Channel<RawMeasurementFrame> _frames = Channel.CreateUnbounded<RawMeasurementFrame>();
        public ValueTask WriteAsync(RawMeasurementFrame frame) => _frames.Writer.WriteAsync(frame);
        public IAsyncEnumerable<RawMeasurementFrame> ReadFramesAsync(CancellationToken cancellationToken) =>
            _frames.Reader.ReadAllAsync(cancellationToken);
    }

    private sealed class SessionFramesDataSource(params RawMeasurementFrame[] frames) : IDataSource
    {
        private int _index;
        public async IAsyncEnumerable<RawMeasurementFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _index) - 1;
            if (index < frames.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return frames[index];
            }
            await Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler<TEvent> : IApplicationEventHandler<TEvent>
    {
        private readonly ConcurrentQueue<TEvent> _events = new();
        public IReadOnlyList<TEvent> Events => _events.ToArray();
        public ValueTask HandleAsync(TEvent applicationEvent, CancellationToken cancellationToken)
        {
            _events.Enqueue(applicationEvent);
            return ValueTask.CompletedTask;
        }
    }
}
