using Application.Services;
using Domain.Alarms;
using Domain.Tags;

namespace Tests.ApplicationTests;

public class AlarmServiceTests
{
    [Fact]
    public void Recover_RemovesCurrentAlarmButKeepsRecentEvent()
    {
        var service = CreateService();
        var triggerTime = DateTimeOffset.UtcNow;
        var firstRecoverTime = triggerTime.AddSeconds(1);
        var secondNormalTime = triggerTime.AddSeconds(2);

        service.Evaluate([CreateValue(25, triggerTime, 1)], triggerTime);
        var alarmId = service.GetCurrentAlarms().Single().AlarmId;

        service.Evaluate([CreateValue(5, firstRecoverTime, 2)], firstRecoverTime);
        service.Evaluate([CreateValue(5, secondNormalTime, 3)], secondNormalTime);

        Assert.Empty(service.GetCurrentAlarms());
        var alarm = service.GetRecentAlarmEvents().Single();
        Assert.Equal(alarmId, alarm.AlarmId);
        Assert.Equal(AlarmState.Recovered, alarm.State);
        Assert.Equal(firstRecoverTime.UtcDateTime, alarm.RecoverTime);
    }

    [Fact]
    public void SameTagCreatesNewAlarmAfterRecovery()
    {
        var service = CreateService();
        var start = DateTimeOffset.UtcNow;

        service.Evaluate([CreateValue(25, start, 1)], start);
        var firstAlarm = service.GetCurrentAlarms().Single();
        service.Evaluate([CreateValue(5, start.AddSeconds(1), 2)], start.AddSeconds(1));

        service.Evaluate([CreateValue(25, start.AddSeconds(2), 3)], start.AddSeconds(2));

        var currentAlarm = service.GetCurrentAlarms().Single();
        var events = service.GetRecentAlarmEvents();
        Assert.NotEqual(firstAlarm.AlarmId, currentAlarm.AlarmId);
        Assert.Equal(AlarmState.Active, currentAlarm.State);
        Assert.Equal(2, events.Count);
        Assert.Contains(events, alarm => alarm.AlarmId == firstAlarm.AlarmId && alarm.State == AlarmState.Recovered);
        Assert.Contains(events, alarm => alarm.AlarmId == currentAlarm.AlarmId && alarm.State == AlarmState.Active);
    }

    [Fact]
    public void Acknowledge_UpdatesCurrentAndRecentEvent()
    {
        var service = CreateService();
        var triggerTime = DateTimeOffset.UtcNow;
        var acknowledgeTime = triggerTime.AddSeconds(5).UtcDateTime;

        service.Evaluate([CreateValue(25, triggerTime, 1)], triggerTime);
        var alarmId = service.GetCurrentAlarms().Single().AlarmId;

        var acknowledged = service.Acknowledge(alarmId, acknowledgeTime);

        Assert.True(acknowledged);
        var currentAlarm = service.GetCurrentAlarms().Single();
        var recentAlarm = service.GetRecentAlarmEvents().Single();
        Assert.Equal(AlarmState.Acknowledged, currentAlarm.State);
        Assert.Equal(acknowledgeTime, currentAlarm.AcknowledgeTime);
        Assert.Equal(AlarmState.Acknowledged, recentAlarm.State);
        Assert.Equal(acknowledgeTime, recentAlarm.AcknowledgeTime);
    }

    [Fact]
    public void ExistingCurrentAlarm_UpdatesLevelValueAndMessage()
    {
        var service = CreateService();
        var start = DateTimeOffset.UtcNow;

        service.Evaluate([CreateValue(15, start, 1)], start);
        var warning = service.GetCurrentAlarms().Single();

        service.Evaluate([CreateValue(25, start.AddSeconds(1), 2)], start.AddSeconds(1));

        var upgraded = service.GetCurrentAlarms().Single();
        Assert.Equal(warning.AlarmId, upgraded.AlarmId);
        Assert.Equal(AlarmLevel.Alarm, upgraded.Level);
        Assert.Equal(25, upgraded.TriggerValue);
        Assert.Contains(nameof(TagAlarmState.AlarmHigh), upgraded.Message);
    }

    [Fact]
    public void RestoreEvents_RehydratesRecentAndNewestCurrentAlarmPerTag()
    {
        var service = CreateService();
        var start = DateTime.UtcNow;
        var recovered = new AlarmEvent(
            Guid.NewGuid(),
            "TEST.TAG",
            AlarmLevel.Warning,
            AlarmState.Recovered,
            12,
            start,
            "Recovered",
            RecoverTime: start.AddSeconds(1));
        var current = new AlarmEvent(
            Guid.NewGuid(),
            "TEST.TAG",
            AlarmLevel.Alarm,
            AlarmState.Acknowledged,
            25,
            start.AddSeconds(2),
            "Current",
            AcknowledgeTime: start.AddSeconds(3));

        service.RestoreEvents([recovered, current]);

        Assert.Equal(2, service.GetAlarmEvents().Count);
        var restoredCurrent = Assert.Single(service.GetCurrentAlarms());
        Assert.Equal(current.AlarmId, restoredCurrent.AlarmId);
        Assert.Equal(AlarmState.Acknowledged, restoredCurrent.State);
    }

    private static AlarmService CreateService() =>
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
}
