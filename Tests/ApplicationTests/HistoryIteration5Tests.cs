using Application.Abstractions.Persistence;
using Application.Configuration;
using Application.Events;
using Application.Queues;
using Application.Services;
using Application.UseCases.Export;
using Domain.Tags;
using Infrastructure.Export;
using Infrastructure.Persistence;
using Tests.Support;

namespace Tests.ApplicationTests;

public sealed class HistoryIteration5Tests
{
    [Fact]
    public async Task Sampling_UsesIntervalAndPreservesQualityAndAlarmBoundaries()
    {
        var definition = CreateDefinition();
        var store = new TagRuntimeConfigurationStore([
            TagRuntimeConfiguration.FromDefinition(definition) with { HistoryIntervalMs = 1000 }
        ]);
        var queue = new HistorySampleQueue();
        var consumer = new HistoryRuntimeStateConsumer(
            new HistoryService(new InMemoryHistoryRepository(), queue),
            store);
        var start = new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

        await consumer.HandleAsync(CreateEvent(start, TagQuality.Good, TagAlarmState.Normal, 1), CancellationToken.None);
        await consumer.HandleAsync(CreateEvent(start.AddMilliseconds(500), TagQuality.Good, TagAlarmState.Normal, 2), CancellationToken.None);
        await consumer.HandleAsync(CreateEvent(start.AddMilliseconds(600), TagQuality.Bad, TagAlarmState.Normal, 3), CancellationToken.None);
        await consumer.HandleAsync(CreateEvent(start.AddMilliseconds(700), TagQuality.Bad, TagAlarmState.WarningHigh, 4), CancellationToken.None);
        await consumer.HandleAsync(CreateEvent(start.AddMilliseconds(800), TagQuality.Bad, TagAlarmState.WarningHigh, 5), CancellationToken.None);
        await consumer.HandleAsync(CreateEvent(start.AddMilliseconds(1700), TagQuality.Bad, TagAlarmState.WarningHigh, 6), CancellationToken.None);

        var samples = Drain(queue);
        Assert.Equal([1L, 3L, 4L, 6L], samples.Select(sample => sample.SequenceNo));
    }

    [Fact]
    public async Task RepositoryQuery_ReturnsStablePagesAndTotalCount()
    {
        var repository = new InMemoryHistoryRepository();
        var start = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
        await repository.AppendAsync(
            Enumerable.Range(1, 5).Select(index => CreateSample(start.AddSeconds(index), index)).ToArray(),
            CancellationToken.None);

        var result = await repository.QueryAsync(
            new HistoryQuery("TEST.HISTORY", start, start.AddMinutes(1), 2, 2),
            CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal([3L, 4L], result.Items.Select(sample => sample.SequenceNo));
        Assert.True(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public async Task CsvExport_ReadsEveryMatchingPage()
    {
        var repository = new InMemoryHistoryRepository();
        var start = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
        const int sampleCount = HistoryQuery.MaximumPageSize + 25;
        await repository.AppendAsync(
            Enumerable.Range(1, sampleCount).Select(index => CreateSample(start.AddMilliseconds(index), index)).ToArray(),
            CancellationToken.None);
        var clock = new TestClock(start.AddHours(1));
        var operationLogs = new OperationLogService(
            new InMemoryOperationLogRepository(),
            new OperationLogQueue(),
            clock);
        var useCase = new ExportHistoryCsvUseCase(new SimpleCsvExporter(), repository, operationLogs);
        var filePath = Path.Combine(Path.GetTempPath(), $"history-{Guid.NewGuid():N}.csv");
        try
        {
            var exported = await useCase.ExecuteAsync(
                new HistoryQuery("TEST.HISTORY", start, start.AddHours(1), 1, 10),
                filePath,
                TimeZoneInfo.Utc,
                CancellationToken.None);

            Assert.Equal(sampleCount, exported);
            Assert.Equal(sampleCount + 1, File.ReadLines(filePath).Count());
            var header = File.ReadLines(filePath).First();
            Assert.Contains("TimestampUtc", header);
            Assert.Contains("TimestampLocal", header);
            Assert.Contains("LocalTimeZone", header);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task RetentionCleanup_DeletesExpiredSamplesInBatches()
    {
        var repository = new InMemoryHistoryRepository();
        var now = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
        await repository.AppendAsync([
            CreateSample(now.AddDays(-31), 1),
            CreateSample(now.AddDays(-30).AddSeconds(-1), 2),
            CreateSample(now.AddDays(-10), 3)
        ], CancellationToken.None);
        var service = new HistoryRetentionService(
            repository,
            new OperationLogService(new InMemoryOperationLogRepository(), new OperationLogQueue(), new TestClock(now)),
            new TestClock(now),
            retentionDays: 30,
            deleteBatchSize: 1);

        var result = await service.CleanupAsync(CancellationToken.None);
        var remaining = await repository.QueryAsync(
            new HistoryQuery("TEST.HISTORY", now.AddDays(-365), now),
            CancellationToken.None);

        Assert.Equal(2, result.DeletedCount);
        Assert.Single(remaining.Items);
        Assert.Equal(3, remaining.Items[0].SequenceNo);
    }

    private static TagDefinition CreateDefinition() => new(
        "TEST.HISTORY",
        "History",
        TagCategory.Measurement,
        "u",
        IsHistorized: true,
        HistoryIntervalMs: 1000,
        DataType: TagDataType.Double);

    private static TagRuntimeStatesProducedEvent CreateEvent(
        DateTimeOffset timestamp,
        TagQuality quality,
        TagAlarmState alarmState,
        long sequenceNo)
    {
        var frameId = Guid.NewGuid();
        return new TagRuntimeStatesProducedEvent(frameId, sequenceNo, timestamp.UtcDateTime, [
            new TagRuntimeState(
                "TEST.HISTORY", "History", TagCategory.Measurement, sequenceNo, null, null, "u",
                TagDataType.Double, quality, alarmState, timestamp, frameId, sequenceNo, timestamp)
        ]);
    }

    private static TagValue CreateSample(DateTime timestampUtc, long sequenceNo) => new(
        "TEST.HISTORY",
        sequenceNo,
        timestampUtc,
        TagQuality.Good,
        TagAlarmState.Normal,
        Guid.NewGuid().ToString("D"),
        sequenceNo);

    private static IReadOnlyList<TagValue> Drain(HistorySampleQueue queue)
    {
        var result = new List<TagValue>();
        while (queue.TryDequeue(out var sample)) result.Add(sample);
        return result;
    }
}
