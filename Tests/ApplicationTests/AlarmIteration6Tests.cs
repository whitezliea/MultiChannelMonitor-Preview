using Application.DTOs.Alarms;
using Application.BackgroundWorkers;
using Application.Caches;
using Application.Events;
using Application.Queues;
using Application.Services;
using Application.UseCases.Alarms;
using Domain.Alarms;
using Domain.Tags;
using Infrastructure.Persistence;
using Tests.Support;

namespace Tests.ApplicationTests;

public sealed class AlarmIteration6Tests
{
    [Fact]
    public void SameLevelDifferentAlarmType_PublishesUpdated()
    {
        var service = CreateService();
        var start = DateTimeOffset.UtcNow;
        service.EvaluateWithChanges([CreateValue(15, start, 1)], start);

        var result = service.EvaluateWithChanges([CreateValue(-15, start.AddSeconds(1), 2)], start.AddSeconds(1));

        var change = Assert.Single(result.LifecycleChanges);
        Assert.Equal(AlarmLifecycleChangeType.Updated, change.ChangeType);
        Assert.Equal(TagAlarmState.WarningLow, change.Alarm.AlarmType);
        Assert.Equal(AlarmLevel.Warning, change.Alarm.Level);
    }

    [Fact]
    public void ValueOnlyUpdate_IsThrottledButPublishedAfterMinimumInterval()
    {
        var service = CreateService();
        var start = DateTimeOffset.UtcNow;
        service.EvaluateWithChanges([CreateValue(15, start, 1)], start);

        var smallImmediateChange = service.EvaluateWithChanges(
            [CreateValue(15.1, start.AddSeconds(1), 2)],
            start.AddSeconds(1));
        var timedChange = service.EvaluateWithChanges(
            [CreateValue(15.1, start.AddSeconds(5), 3)],
            start.AddSeconds(5));

        Assert.Empty(smallImmediateChange.LifecycleChanges);
        var update = Assert.Single(timedChange.LifecycleChanges);
        Assert.Equal(AlarmLifecycleChangeType.Updated, update.ChangeType);
        Assert.Equal(15.1, update.Alarm.TriggerValue);
        Assert.Equal(start.AddSeconds(5).UtcDateTime, update.Alarm.LastUpdatedTime);
    }

    [Fact]
    public void Recovery_RecordsCloseReasonAndLastUpdatedTime()
    {
        var service = CreateService();
        var start = DateTimeOffset.UtcNow;
        service.EvaluateWithChanges([CreateValue(25, start, 1)], start);

        var result = service.EvaluateWithChanges([CreateValue(0, start.AddSeconds(1), 2)], start.AddSeconds(1));

        var recovered = Assert.Single(result.LifecycleChanges).Alarm;
        Assert.Equal(AlarmState.Recovered, recovered.State);
        Assert.Equal("ValueReturnedToNormal", recovered.CloseReason);
        Assert.Equal(start.AddSeconds(1).UtcDateTime, recovered.LastUpdatedTime);
    }

    [Fact]
    public async Task AcknowledgeWhileAcquisitionIsStopped_IsPersistedByApplicationWorker()
    {
        var service = CreateService();
        var start = DateTimeOffset.UtcNow;
        service.EvaluateWithChanges([CreateValue(25, start, 1)], start);
        var alarmId = service.GetCurrentAlarms().Single().AlarmId;
        var repository = new InMemoryAlarmRepository();
        var queue = new AlarmEventQueue();
        var consumer = new AlarmEventConsumer(queue);
        var publisher = new ApplicationEventPublisher();
        publisher.Register<AlarmAcknowledgedEvent>(consumer);
        var worker = new AlarmPersistWorker(queue, repository, TimeSpan.FromMilliseconds(20));
        await using var persistence = new PersistenceRuntimeCoordinator(worker);
        Assert.True(await persistence.StartAsync());
        var useCase = new AcknowledgeAlarmUseCase(
            service,
            publisher,
            new TestClock(start.AddSeconds(1).UtcDateTime));

        Assert.True(await useCase.ExecuteAsync(alarmId));
        var timeout = DateTime.UtcNow.AddSeconds(2);
        IReadOnlyList<AlarmEvent> open;
        do
        {
            open = await repository.QueryOpenAlarmsAsync(CancellationToken.None);
            if (open.Count == 0) await Task.Delay(10);
        }
        while (open.Count == 0 && DateTime.UtcNow < timeout);

        var persisted = Assert.Single(open);
        Assert.Equal(AlarmState.Acknowledged, persisted.State);
        Assert.Equal(start.AddSeconds(1).UtcDateTime, persisted.AcknowledgeTime);
    }

    [Fact]
    public async Task WarningToAlarmUpdate_FlowsThroughEventQueueAndUpsertsRepository()
    {
        var service = CreateService();
        var repository = new InMemoryAlarmRepository();
        var queue = new AlarmEventQueue();
        var consumer = new AlarmEventConsumer(queue);
        var publisher = new ApplicationEventPublisher();
        publisher.Register<AlarmRaisedEvent>(consumer);
        publisher.Register<AlarmUpdatedEvent>(consumer);
        var worker = new AlarmPersistWorker(queue, repository, TimeSpan.FromMilliseconds(20));
        await using var persistence = new PersistenceRuntimeCoordinator(worker);
        Assert.True(await persistence.StartAsync());
        var start = DateTimeOffset.UtcNow;

        var raised = Assert.Single(service.EvaluateWithChanges(
            [CreateValue(15, start, 1)], start).LifecycleChanges);
        await publisher.PublishAsync(new AlarmRaisedEvent(raised.Alarm), CancellationToken.None);
        var updated = Assert.Single(service.EvaluateWithChanges(
            [CreateValue(25, start.AddSeconds(1), 2)], start.AddSeconds(1)).LifecycleChanges);
        await publisher.PublishAsync(new AlarmUpdatedEvent(updated.Alarm), CancellationToken.None);

        var timeout = DateTime.UtcNow.AddSeconds(2);
        AlarmEvent? persisted = null;
        while (DateTime.UtcNow < timeout)
        {
            persisted = (await repository.QueryOpenAlarmsAsync(CancellationToken.None)).SingleOrDefault();
            if (persisted?.Level == AlarmLevel.Alarm) break;
            await Task.Delay(10);
        }

        Assert.NotNull(persisted);
        Assert.Equal(AlarmLevel.Alarm, persisted.Level);
        Assert.Equal(TagAlarmState.AlarmHigh, persisted.AlarmType);
        Assert.Equal(raised.Alarm.AlarmId, persisted.AlarmId);
    }

    private static AlarmService CreateService() => new([
        new TagDefinition(
            "TEST.TAG",
            "Test Tag",
            TagCategory.Measurement,
            "u",
            WarningLow: -10,
            AlarmLow: -20,
            WarningHigh: 10,
            AlarmHigh: 20)
    ]);

    private static CleanedTagValue CreateValue(double value, DateTimeOffset timestamp, long sequenceNo) => new(
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
}
