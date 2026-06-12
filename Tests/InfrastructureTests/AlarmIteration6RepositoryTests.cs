using Application.Abstractions.Persistence;
using Domain.Alarms;
using Domain.Tags;
using Infrastructure.Persistence;

namespace Tests.InfrastructureTests;

public sealed class AlarmIteration6RepositoryTests
{
    [Fact]
    public async Task OpenAlarmQuery_IsNotLimitedByRecentCount()
    {
        var repository = CreateRepository();
        var start = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldOpen = CreateAlarm(start, AlarmState.Active, "OPEN", 0);
        var recovered = Enumerable.Range(1, 1001)
            .Select(index => CreateAlarm(start.AddMinutes(index), AlarmState.Recovered, $"CLOSED-{index}", index))
            .ToArray();
        await repository.AppendAsync([oldOpen, .. recovered], CancellationToken.None);

        var latest = await repository.QueryLatestAsync(1000, CancellationToken.None);
        var open = await repository.QueryOpenAlarmsAsync(CancellationToken.None);

        Assert.DoesNotContain(latest, alarm => alarm.AlarmId == oldOpen.AlarmId);
        Assert.Equal(oldOpen.AlarmId, Assert.Single(open).AlarmId);
    }

    [Fact]
    public async Task UpdatedAlarm_UpsertsLevelTypeAndLastUpdatedTime()
    {
        var repository = CreateRepository();
        var start = DateTime.UtcNow;
        var raised = CreateAlarm(start, AlarmState.Active, "TAG.A", 15) with
        {
            Level = AlarmLevel.Warning,
            AlarmType = TagAlarmState.WarningHigh,
            LastUpdatedTime = start
        };
        var updated = raised with
        {
            Level = AlarmLevel.Alarm,
            AlarmType = TagAlarmState.AlarmHigh,
            TriggerValue = 25,
            LastUpdatedTime = start.AddSeconds(1),
            Message = "Upgraded"
        };
        await repository.AppendAsync([raised], CancellationToken.None);
        await repository.AppendAsync([updated], CancellationToken.None);

        var persisted = Assert.Single(await repository.QueryOpenAlarmsAsync(CancellationToken.None));

        Assert.Equal(AlarmLevel.Alarm, persisted.Level);
        Assert.Equal(TagAlarmState.AlarmHigh, persisted.AlarmType);
        Assert.Equal(25, persisted.TriggerValue);
        Assert.Equal(start.AddSeconds(1), persisted.LastUpdatedTime);
    }

    [Fact]
    public async Task HistoryQuery_FiltersAndPagesPersistedAlarms()
    {
        var repository = CreateRepository();
        var start = new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc);
        await repository.AppendAsync([
            CreateAlarm(start.AddMinutes(1), AlarmState.Recovered, "TAG.A", 1) with { Level = AlarmLevel.Warning },
            CreateAlarm(start.AddMinutes(2), AlarmState.Recovered, "TAG.A", 2) with { Level = AlarmLevel.Warning },
            CreateAlarm(start.AddMinutes(3), AlarmState.Active, "TAG.A", 3) with { Level = AlarmLevel.Alarm },
            CreateAlarm(start.AddMinutes(4), AlarmState.Recovered, "TAG.B", 4) with { Level = AlarmLevel.Warning }
        ], CancellationToken.None);

        var first = await repository.QueryAsync(
            new AlarmQuery(start, start.AddHours(1), "TAG.A", AlarmLevel.Warning, AlarmState.Recovered, 1, 1),
            CancellationToken.None);
        var second = await repository.QueryAsync(
            new AlarmQuery(start, start.AddHours(1), "TAG.A", AlarmLevel.Warning, AlarmState.Recovered, 2, 1),
            CancellationToken.None);

        Assert.Equal(2, first.TotalCount);
        Assert.True(first.HasNextPage);
        Assert.Equal(2, first.Items[0].TriggerValue);
        Assert.Equal(1, second.Items[0].TriggerValue);
    }

    private static SQLiteAlarmRepository CreateRepository()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MultiChannelMonitor.Tests", Guid.NewGuid().ToString("N"));
        return new SQLiteAlarmRepository(new SqliteConnectionFactory(Path.Combine(directory, "alarm.db")));
    }

    private static AlarmEvent CreateAlarm(
        DateTime triggerTimeUtc,
        AlarmState state,
        string tagId,
        double value) => new(
            Guid.NewGuid(),
            tagId,
            AlarmLevel.Alarm,
            state,
            value,
            triggerTimeUtc,
            tagId,
            RecoverTime: state == AlarmState.Recovered ? triggerTimeUtc.AddSeconds(1) : null,
            AlarmType: TagAlarmState.AlarmHigh,
            LastUpdatedTime: triggerTimeUtc.AddSeconds(1),
            CloseReason: state == AlarmState.Recovered ? "ValueReturnedToNormal" : null);
}
