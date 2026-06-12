using Application.BackgroundWorkers;
using Application.Events;
using Application.Queues;
using Application.Services;
using Application.UseCases.Logs;
using Domain.Alarms;
using Domain.Logs;
using Infrastructure.Persistence;
using Tests.Support;

namespace Tests.ApplicationTests;

public sealed class OperationLogPipelineTests
{
    [Fact]
    public async Task AlarmEventConsumer_WritesLifecycleOperationLog()
    {
        var repository = new InMemoryOperationLogRepository();
        var queue = new OperationLogQueue();
        var clock = new TestClock(DateTime.UtcNow);
        var service = new OperationLogService(repository, queue, clock);
        var consumer = new AlarmOperationLogConsumer(service);
        var alarm = CreateAlarm();

        await consumer.HandleAsync(new AlarmAcknowledgedEvent(alarm), CancellationToken.None);
        var log = await queue.DequeueAsync(CancellationToken.None);

        Assert.Equal("Alarm.Acknowledged", log.Action);
        Assert.Equal(alarm.AlarmId.ToString("D"), log.CorrelationId);
        Assert.Contains(alarm.TagId, log.Detail);
        Assert.Equal(DateTimeKind.Utc, log.Timestamp.Kind);
    }

    [Fact]
    public async Task QueryUseCase_FlushesPendingLogsBeforeReadingRepository()
    {
        var repository = new InMemoryOperationLogRepository();
        var queue = new OperationLogQueue();
        var clock = new TestClock(DateTime.UtcNow);
        var service = new OperationLogService(repository, queue, clock);
        var worker = new OperationLogPersistWorker(queue, repository, TimeSpan.FromSeconds(30));
        await using var persistence = new PersistenceRuntimeCoordinator(worker);
        Assert.True(await persistence.StartAsync());
        await service.WriteAsync(
            OperationLogLevel.Info,
            "Acquisition",
            "Acquisition.Started",
            "test",
            "Started");
        var useCase = new QueryOperationLogsUseCase(service, persistence);

        var result = await useCase.ExecuteAsync(new OperationLogQuery(
            clock.UtcNow.AddMinutes(-1),
            clock.UtcNow.AddMinutes(1),
            Category: "Acquisition"));

        Assert.Single(result);
        Assert.Equal("Acquisition.Started", result[0].Action);
    }

    [Fact]
    public async Task ApplicationRuntimeHost_PersistsStartupAndExitLogsDuringShutdown()
    {
        var repository = new InMemoryOperationLogRepository();
        var queue = new OperationLogQueue();
        var service = new OperationLogService(repository, queue, new TestClock(DateTime.UtcNow));
        var worker = new OperationLogPersistWorker(queue, repository, TimeSpan.FromSeconds(30));
        var persistence = new PersistenceRuntimeCoordinator(worker);
        var host = new ApplicationRuntimeHost(persistence, service);

        await host.StartAsync();
        await host.DisposeAsync();

        var logs = await repository.QueryLatestAsync(10, CancellationToken.None);
        Assert.Contains(logs, log => log.Action == "Application.Started");
        Assert.Contains(logs, log => log.Action == "Application.Exiting");
    }

    private static AlarmEvent CreateAlarm()
    {
        var now = DateTime.UtcNow;
        return new AlarmEvent(
            Guid.NewGuid(),
            "TEST.TAG",
            AlarmLevel.Warning,
            AlarmState.Acknowledged,
            12.5,
            now,
            "Test alarm",
            now.AddSeconds(1));
    }
}
