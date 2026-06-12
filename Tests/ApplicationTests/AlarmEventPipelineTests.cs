using System.Runtime.CompilerServices;
using Application.Abstractions.DataSource;
using Application.Abstractions.Events;
using Application.Caches;
using Application.DTOs.Alarms;
using Application.Events;
using Application.Pipelines;
using Application.Queues;
using Application.Services;
using Application.UseCases.Alarms;
using Domain.Alarms;
using Domain.Measurements;
using Domain.Tags;
using Simulator.Generators;
using Simulator.Scenarios;
using Tests.Support;

namespace Tests.ApplicationTests;

public class AlarmEventPipelineTests
{
    [Fact]
    public void EvaluateWithChanges_ReportsRaiseAndRecoverOnlyOnce()
    {
        var service = CreateAlarmService();
        var start = DateTimeOffset.UtcNow;

        var raised = service.EvaluateWithChanges([CreateValue(25, start, 1)], start);
        var repeatedAlarm = service.EvaluateWithChanges([CreateValue(26, start.AddSeconds(1), 2)], start.AddSeconds(1));
        var recovered = service.EvaluateWithChanges([CreateValue(5, start.AddSeconds(2), 3)], start.AddSeconds(2));
        var repeatedNormal = service.EvaluateWithChanges([CreateValue(5, start.AddSeconds(3), 4)], start.AddSeconds(3));

        var raisedChange = Assert.Single(raised.LifecycleChanges);
        Assert.Equal(AlarmLifecycleChangeType.Raised, raisedChange.ChangeType);
        Assert.Equal(AlarmState.Active, raisedChange.Alarm.State);
        Assert.Empty(repeatedAlarm.LifecycleChanges);

        var recoveredChange = Assert.Single(recovered.LifecycleChanges);
        Assert.Equal(AlarmLifecycleChangeType.Recovered, recoveredChange.ChangeType);
        Assert.Equal(raisedChange.Alarm.AlarmId, recoveredChange.Alarm.AlarmId);
        Assert.Equal(AlarmState.Recovered, recoveredChange.Alarm.State);
        Assert.Empty(repeatedNormal.LifecycleChanges);
    }

    [Fact]
    public async Task AlarmEventConsumer_UpsertsSessionCacheAndQueuesEveryLifecycleSnapshot()
    {
        var queue = new AlarmEventQueue();
        var consumer = new AlarmEventConsumer(queue);
        var publisher = new ApplicationEventPublisher();
        publisher.Register<AlarmRaisedEvent>(consumer);
        publisher.Register<AlarmAcknowledgedEvent>(consumer);
        publisher.Register<AlarmRecoveredEvent>(consumer);

        var raised = CreateAlarm(AlarmState.Active);
        var acknowledged = raised with
        {
            State = AlarmState.Acknowledged,
            AcknowledgeTime = raised.TriggerTime.AddSeconds(1)
        };
        var recovered = acknowledged with
        {
            State = AlarmState.Recovered,
            RecoverTime = raised.TriggerTime.AddSeconds(2)
        };

        await publisher.PublishAsync(new AlarmRaisedEvent(raised), CancellationToken.None);
        await publisher.PublishAsync(new AlarmAcknowledgedEvent(acknowledged), CancellationToken.None);
        await publisher.PublishAsync(new AlarmRecoveredEvent(recovered), CancellationToken.None);

        var queued = await ReadItemsAsync(queue, 3);
        Assert.Equal(
            [AlarmState.Active, AlarmState.Acknowledged, AlarmState.Recovered],
            queued.Select(alarm => alarm.State));
        Assert.All(queued, alarm => Assert.Equal(raised.AlarmId, alarm.AlarmId));
    }

    [Fact]
    public async Task AcknowledgeUseCase_PublishesAcknowledgedEventOnlyOnce()
    {
        var service = CreateAlarmService();
        var start = DateTimeOffset.UtcNow;
        service.Evaluate([CreateValue(25, start, 1)], start);
        var alarmId = service.GetCurrentAlarms().Single().AlarmId;

        var publisher = new ApplicationEventPublisher();
        var recorder = new RecordingHandler<AlarmAcknowledgedEvent>();
        publisher.Register(recorder);
        var useCase = new AcknowledgeAlarmUseCase(
            service,
            publisher,
            new TestClock(start.AddSeconds(1).UtcDateTime));

        var firstResult = await useCase.ExecuteAsync(alarmId, start.AddSeconds(1).UtcDateTime);
        var secondResult = await useCase.ExecuteAsync(alarmId, start.AddSeconds(2).UtcDateTime);

        Assert.True(firstResult);
        Assert.False(secondResult);
        var published = Assert.Single(recorder.Events);
        Assert.Equal(alarmId, published.Alarm.AlarmId);
        Assert.Equal(AlarmState.Acknowledged, published.Alarm.State);
        Assert.Equal(start.AddSeconds(1).UtcDateTime, published.Alarm.AcknowledgeTime);
    }

    [Fact]
    public async Task AlarmHandlerFailure_DoesNotPreventTagCacheUpdate()
    {
        var definitions = TagDefinitionCatalog.CreateDefaults();
        var start = DateTime.UtcNow;
        var frame = new FakeDataGenerator("MCMD-TEST", new AlarmScenario(), start)
            .NextFrame(start.AddSeconds(1));
        var clock = new TestClock(start.AddSeconds(2));
        var tagService = new TagService(new TagCache(100), clock);
        var publisher = new ApplicationEventPublisher();
        publisher.Register(new TagCacheConsumer(tagService));
        publisher.Register<AlarmRaisedEvent>(
            new ThrowingHandler<AlarmRaisedEvent>(),
            ApplicationEventHandlerFailurePolicy.Isolated);
        var runtime = new MonitoringRuntimeService(
            new DataSourceService(new FixedDataSource(frame)),
            new DataCleanPipeline(definitions),
            new AlarmService(definitions),
            publisher,
            clock);

        await runtime.RunAsync(CancellationToken.None);

        var snapshot = tagService.GetSnapshot();
        Assert.NotEmpty(snapshot.CurrentValues);
        Assert.Contains(snapshot.CurrentValues, state => state.AlarmState != TagAlarmState.Normal);
    }

    private static AlarmService CreateAlarmService() =>
        new([
            new TagDefinition(
                "TEST.TAG",
                "Test Tag",
                TagCategory.Measurement,
                "u",
                WarningHigh: 10,
                AlarmHigh: 20)
        ]);

    private static CleanedTagValue CreateValue(double value, DateTimeOffset timestamp, long sequenceNo) =>
        new(
            "TEST.TAG",
            value,
            null,
            null,
            TagDataType.Double,
            "u",
            timestamp,
            TagQuality.Good,
            "TEST",
            "TEST_TAG",
            Guid.NewGuid(),
            sequenceNo,
            null);

    private static AlarmEvent CreateAlarm(AlarmState state) =>
        new(
            Guid.NewGuid(),
            "TEST.TAG",
            AlarmLevel.Alarm,
            state,
            25,
            DateTime.UtcNow,
            "Test alarm");

    private static async Task<IReadOnlyList<AlarmEvent>> ReadItemsAsync(AlarmEventQueue queue, int count)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var enumerator = queue.ReadAllAsync(cancellation.Token).GetAsyncEnumerator();
        var items = new List<AlarmEvent>(count);
        while (items.Count < count && await enumerator.MoveNextAsync())
        {
            items.Add(enumerator.Current);
        }

        return items;
    }

    private sealed class RecordingHandler<TEvent> : IApplicationEventHandler<TEvent>
    {
        public List<TEvent> Events { get; } = [];

        public ValueTask HandleAsync(TEvent applicationEvent, CancellationToken cancellationToken)
        {
            Events.Add(applicationEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingHandler<TEvent> : IApplicationEventHandler<TEvent>
    {
        public ValueTask HandleAsync(TEvent applicationEvent, CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("Expected test failure."));
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
