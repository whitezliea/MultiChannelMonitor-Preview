using System.Runtime.CompilerServices;
using Application.Abstractions.DataSource;
using Application.Pipelines;
using Application.Services;
using Application.UseCases.Alarms;
using Domain.Alarms;
using Domain.Devices;
using Domain.Measurements;
using Domain.Tags;
using Tests.Support;

namespace Tests.ApplicationTests;

public sealed class UtcTimeChainTests
{
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task DataSourceService_RejectsNonUtcRawFrames(DateTimeKind kind)
    {
        var timestamp = DateTime.SpecifyKind(DateTime.UtcNow, kind);
        var frame = CreateFrame(timestamp);
        var service = new DataSourceService(new FixedDataSource(frame));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in service.ReadFramesAsync(CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public async Task DataSourceService_RejectsMatrixTimestampThatDiffersFromRawFrame()
    {
        var timestamp = DateTime.UtcNow;
        var frame = CreateFrame(timestamp) with
        {
            MatrixValues = new MatrixFrame(
                Guid.NewGuid(),
                timestamp.AddMilliseconds(1),
                1,
                1,
                new double[,] { { 1 } })
        };
        var service = new DataSourceService(new FixedDataSource(frame));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in service.ReadFramesAsync(CancellationToken.None))
            {
            }
        });
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void DataCleanPipeline_RejectsNonUtcFrameWhenCalledDirectly(DateTimeKind kind)
    {
        var timestamp = DateTime.SpecifyKind(DateTime.UtcNow, kind);
        var pipeline = new DataCleanPipeline(TagDefinitionCatalog.CreateDefaults());

        Assert.Throws<ArgumentException>(() => pipeline.CleanToCleanedValues(CreateFrame(timestamp)));
    }

    [Fact]
    public async Task AcknowledgeAlarmUseCase_UsesInjectedUtcClock()
    {
        var definition = new TagDefinition(
            "TEST.TAG",
            "Test",
            TagCategory.Measurement,
            "u",
            AlarmHigh: 20);
        var service = new AlarmService([definition]);
        var triggerTime = DateTimeOffset.UtcNow;
        service.Evaluate(
            [CreateAlarmValue(triggerTime)],
            triggerTime);
        var alarmId = service.GetCurrentAlarms().Single().AlarmId;
        var acknowledgedAt = triggerTime.AddSeconds(3).UtcDateTime;
        var useCase = new AcknowledgeAlarmUseCase(
            service,
            new ApplicationEventPublisher(),
            new TestClock(acknowledgedAt));

        var acknowledged = await useCase.ExecuteAsync(alarmId);

        Assert.True(acknowledged);
        Assert.Equal(acknowledgedAt, service.GetCurrentAlarms().Single().AcknowledgeTime);
        Assert.Equal(DateTimeKind.Utc, service.GetCurrentAlarms().Single().AcknowledgeTime?.Kind);
    }

    private static RawMeasurementFrame CreateFrame(DateTime timestamp) =>
        new(
            Guid.NewGuid(),
            "TEST",
            1,
            timestamp,
            DeviceStatus.Running,
            [],
            new MatrixFrame(Guid.NewGuid(), timestamp, 1, 1, new double[,] { { 1 } }),
            0,
            TagQuality.Good);

    private static CleanedTagValue CreateAlarmValue(DateTimeOffset timestamp) =>
        new(
            "TEST.TAG",
            25,
            null,
            null,
            TagDataType.Double,
            "u",
            timestamp,
            TagQuality.Good,
            "TEST",
            "TEST",
            Guid.NewGuid(),
            1,
            null);

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
