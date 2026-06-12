using Application.Abstractions.Persistence;
using Application.Configuration;
using Application.Queues;
using Application.Services;
using Domain.Alarms;
using Domain.Logs;
using Domain.Tags;
using Infrastructure.Persistence;
using Tests.Support;

namespace Tests.ApplicationTests;

public sealed class ConfigurationServiceTests
{
    [Fact]
    public void AlarmThresholdChange_TakesEffectOnNextEvaluationAndPublishesUpdate()
    {
        var definition = CreateDefinition();
        var store = CreateStore(definition);
        var service = new AlarmService([definition], store);
        var now = DateTimeOffset.UtcNow;
        service.EvaluateWithChanges([CreateValue(15, now, 1)], now);
        store.Replace([store.Get(definition.TagId) with { WarningHigh = 5, AlarmHigh = 10 }]);

        var result = service.EvaluateWithChanges([CreateValue(15, now.AddSeconds(1), 2)], now.AddSeconds(1));

        var change = Assert.Single(result.LifecycleChanges);
        Assert.Equal(Application.DTOs.Alarms.AlarmLifecycleChangeType.Updated, change.ChangeType);
        Assert.Equal(AlarmLevel.Alarm, change.Alarm.Level);
    }

    [Fact]
    public void DisablingAlarm_RecoversCurrentAlarmWithConfigurationReason()
    {
        var definition = CreateDefinition();
        var store = CreateStore(definition);
        var service = new AlarmService([definition], store);
        var now = DateTimeOffset.UtcNow;
        service.EvaluateWithChanges([CreateValue(25, now, 1)], now);
        store.Replace([store.Get(definition.TagId) with { AlarmEnabled = false }]);

        var result = service.EvaluateWithChanges([CreateValue(25, now.AddSeconds(1), 2)], now.AddSeconds(1));

        var recovered = Assert.Single(result.LifecycleChanges);
        Assert.Equal(Application.DTOs.Alarms.AlarmLifecycleChangeType.Recovered, recovered.ChangeType);
        Assert.Contains("ConfigurationChanged", recovered.Alarm.Message);
        Assert.Empty(service.GetCurrentAlarms());
    }

    [Fact]
    public async Task HistoryIntervalChange_ResetsSamplingBaselineOnNextFrame()
    {
        var definition = CreateDefinition() with { IsHistorized = true, HistoryIntervalMs = 1000 };
        var store = CreateStore(definition);
        var queue = new HistorySampleQueue();
        var consumer = new HistoryRuntimeStateConsumer(
            new HistoryService(new InMemoryHistoryRepository(), queue),
            store);
        var now = DateTimeOffset.UtcNow;
        await consumer.HandleAsync(CreateStatesEvent(now, 1), CancellationToken.None);
        Assert.True(queue.TryDequeue(out _));
        await consumer.HandleAsync(CreateStatesEvent(now.AddMilliseconds(100), 2), CancellationToken.None);
        Assert.False(queue.TryDequeue(out _));
        store.Replace([store.Get(definition.TagId) with { HistoryIntervalMs = 5000 }]);

        await consumer.HandleAsync(CreateStatesEvent(now.AddMilliseconds(200), 3), CancellationToken.None);

        Assert.True(queue.TryDequeue(out var resetSample));
        Assert.Equal(3, resetSample.SequenceNo);
    }

    [Fact]
    public async Task SaveFailure_DoesNotUpdateStoreOrWriteSuccessLog()
    {
        var definition = CreateDefinition();
        var store = CreateStore(definition);
        var runtimeStore = new RuntimeOptionsStore(new MonitorRuntimeOptions());
        var logQueue = new OperationLogQueue();
        var service = new ConfigurationService(
            [definition],
            new ThrowingConfigurationRepository(),
            store,
            runtimeStore,
            new OperationLogService(new InMemoryOperationLogRepository(), logQueue, new TestClock(DateTime.UtcNow)));
        var original = store.Get(definition.TagId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveTagsAsync([original with { WarningHigh = 8 }], CancellationToken.None));

        Assert.Equal(original.WarningHigh, store.Get(definition.TagId).WarningHigh);
        Assert.False(logQueue.TryDequeue(out _));
    }

    [Fact]
    public async Task RuntimeSave_ReportsExplicitEffectLevels()
    {
        var definition = CreateDefinition();
        var store = CreateStore(definition);
        var runtimeStore = new RuntimeOptionsStore(new MonitorRuntimeOptions());
        var service = new ConfigurationService(
            [definition],
            new RecordingConfigurationRepository(),
            store,
            runtimeStore,
            new OperationLogService(
                new InMemoryOperationLogRepository(),
                new OperationLogQueue(),
                new TestClock(DateTime.UtcNow)));
        var options = runtimeStore.Snapshot with
        {
            UiRefreshInterval = TimeSpan.FromMilliseconds(250),
            DataGenerateInterval = TimeSpan.FromMilliseconds(750)
        };

        var result = await service.SaveRuntimeAsync(options);

        Assert.Equal(SettingEffect.Immediate, result.Effects[RuntimeSettingKeys.UiRefreshIntervalMs]);
        Assert.Equal(SettingEffect.NextAcquisitionStart, result.Effects[RuntimeSettingKeys.DataGenerateIntervalMs]);
        Assert.Equal(SettingEffect.NextApplicationStart, result.Effects[RuntimeSettingKeys.HistoryBatchIntervalMs]);
        Assert.Equal(options, runtimeStore.Snapshot);
    }

    private static TagDefinition CreateDefinition() =>
        new("TEST.TAG", "Test", TagCategory.Measurement, "u", 0, 100, WarningHigh: 10, AlarmHigh: 20);

    private static TagRuntimeConfigurationStore CreateStore(TagDefinition definition) =>
        new([TagRuntimeConfiguration.FromDefinition(definition)]);

    private static CleanedTagValue CreateValue(double value, DateTimeOffset timestamp, long sequenceNo) =>
        new("TEST.TAG", value, null, null, TagDataType.Double, "u", timestamp, TagQuality.Good,
            "TEST", "TEST_TAG", Guid.NewGuid(), sequenceNo, null);

    private static Application.Events.TagRuntimeStatesProducedEvent CreateStatesEvent(
        DateTimeOffset timestamp,
        long sequenceNo) =>
        new(
            Guid.NewGuid(),
            sequenceNo,
            timestamp.UtcDateTime,
            [new TagRuntimeState(
                "TEST.TAG", "Test", TagCategory.Measurement, 1, null, null, "u",
                TagDataType.Double, TagQuality.Good, TagAlarmState.Normal, timestamp,
                Guid.NewGuid(), sequenceNo, timestamp)]);

    private class RecordingConfigurationRepository : IConfigurationRepository
    {
        public Task<IReadOnlyList<TagRuntimeConfiguration>> LoadTagConfigurationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TagRuntimeConfiguration>>([]);
        public virtual Task SaveTagConfigurationsAsync(IReadOnlyCollection<TagRuntimeConfiguration> configurations, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<IReadOnlyDictionary<string, string>> LoadRuntimeSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task SaveRuntimeSettingsAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingConfigurationRepository : RecordingConfigurationRepository
    {
        public override Task SaveTagConfigurationsAsync(IReadOnlyCollection<TagRuntimeConfiguration> configurations, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Expected save failure.");
    }
}
