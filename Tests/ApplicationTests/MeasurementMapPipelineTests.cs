using Application.Abstractions.DataSource;
using Application.Abstractions.Events;
using Application.Caches;
using Application.Events;
using Application.Pipelines;
using Application.Services;
using Domain.Devices;
using Domain.Measurements;
using Domain.Tags;
using System.Runtime.CompilerServices;
using Tests.Support;

namespace Tests.ApplicationTests;

public class MeasurementMapPipelineTests
{
    [Fact]
    public async Task RuntimeService_ShouldPublishRawMatrixBodyToMeasurementMapService()
    {
        var values = new double[,]
        {
            { 1, 2 },
            { 3, 4 }
        };
        var timestamp = DateTime.UtcNow;
        var clock = new TestClock(timestamp.AddSeconds(1));
        var frame = new RawMeasurementFrame(
            Guid.NewGuid(),
            "MCMD-001",
            1,
            timestamp,
            DeviceStatus.Running,
            [],
            new MatrixFrame(Guid.NewGuid(), timestamp, 2, 2, values),
            0,
            TagQuality.Good);
        var measurementMapService = new MeasurementMapService(new MatrixFrameCache());
        var eventPublisher = new ApplicationEventPublisher();
        var rawFrameCounter = new CountingHandler<RawFrameReceivedEvent>();
        var stateCounter = new CountingHandler<TagRuntimeStatesProducedEvent>();
        eventPublisher.Register(rawFrameCounter);
        eventPublisher.Register(new MeasurementMapFrameConsumer(measurementMapService));
        eventPublisher.Register(stateCounter);
        eventPublisher.Register(new TagCacheConsumer(new TagService(new TagCache(trendBufferCapacity: 10), clock)));
        var runtimeService = new MonitoringRuntimeService(
            new DataSourceService(new SingleFrameDataSource(frame)),
            new DataCleanPipeline(TagDefinitionCatalog.CreateDefaults()),
            new AlarmService(TagDefinitionCatalog.CreateDefaults()),
            eventPublisher,
            clock);

        await runtimeService.RunAsync(CancellationToken.None);

        var latest = measurementMapService.GetLatest();
        Assert.NotNull(latest);
        Assert.Equal(2, latest.Rows);
        Assert.Equal(2, latest.Columns);
        Assert.Equal(1, latest.Values[0, 0]);
        Assert.Equal(4, latest.Values[1, 1]);
        Assert.Equal(frame.FrameId, latest.SourceFrameId);
        Assert.Equal(frame.SequenceNo, latest.SequenceNo);
        Assert.Equal(2.5, latest.Statistics.AverageValue);
        Assert.Equal(Math.Sqrt(1.25), latest.Statistics.StdDev, precision: 12);
        Assert.Equal(0.25, latest.Statistics.UniformityMinMax);
        Assert.Equal(latest.Statistics.UniformityMinMax, latest.Statistics.Uniformity);
        Assert.Equal(0.4, latest.Statistics.UniformityMinAverage);
        Assert.Equal(4, latest.Statistics.ValidCount);
        Assert.Equal(0, latest.Statistics.InvalidCount);
        Assert.Equal(1, rawFrameCounter.CallCount);
        Assert.Equal(1, stateCounter.CallCount);
        Assert.Equal(frame.FrameId, stateCounter.LastEvent?.SourceFrameId);
        Assert.Contains(stateCounter.LastEvent!.States, state => state.TagId == "MATRIX.LIGHT.AVG");
    }

    [Fact]
    public async Task RuntimeService_ShouldRunWithoutOptionalEventConsumers()
    {
        var frame = new RawMeasurementFrame(
            Guid.NewGuid(), "MCMD-001", 1, DateTime.UtcNow, DeviceStatus.Running,
            [], null, 0, TagQuality.Good);
        var clock = new TestClock(frame.Timestamp.AddSeconds(1));
        var runtimeService = new MonitoringRuntimeService(
            new DataSourceService(new SingleFrameDataSource(frame)),
            new DataCleanPipeline(TagDefinitionCatalog.CreateDefaults()),
            new AlarmService(TagDefinitionCatalog.CreateDefaults()),
            new ApplicationEventPublisher(),
            clock);

        await runtimeService.RunAsync(CancellationToken.None);
    }

    [Fact]
    public void MatrixFrameCache_ShouldIsolateStoredAndReturnedArrays()
    {
        var sourceValues = new double[,] { { 1, 2 }, { 3, 4 } };
        var cache = new MatrixFrameCache();
        cache.Update(new MatrixFrame(Guid.NewGuid(), DateTime.UtcNow, 2, 2, sourceValues));
        sourceValues[0, 0] = 99;

        var firstRead = cache.GetLatest();
        Assert.NotNull(firstRead);
        Assert.Equal(1, firstRead.Values[0, 0]);

        firstRead.Values[0, 0] = 88;
        Assert.Equal(1, cache.GetLatest()!.Values[0, 0]);
    }

    [Fact]
    public void MeasurementMapService_ShouldExposeInvalidValueStatistics()
    {
        var service = new MeasurementMapService(new MatrixFrameCache());
        service.Update(new MatrixFrame(
            Guid.NewGuid(),
            DateTime.UtcNow,
            1,
            3,
            new double[,] { { 10, double.NaN, double.PositiveInfinity } }));

        var latest = service.GetLatest();

        Assert.NotNull(latest);
        Assert.Equal(10, latest.Statistics.MinValue);
        Assert.Equal(10, latest.Statistics.MaxValue);
        Assert.Equal(10, latest.Statistics.AverageValue);
        Assert.Equal(0, latest.Statistics.StdDev);
        Assert.Equal(1, latest.Statistics.ValidCount);
        Assert.Equal(2, latest.Statistics.InvalidCount);
    }

    private sealed class SingleFrameDataSource : IDataSource
    {
        private readonly RawMeasurementFrame _frame;

        public SingleFrameDataSource(RawMeasurementFrame frame)
        {
            _frame = frame;
        }

        public async IAsyncEnumerable<RawMeasurementFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return _frame;
            await Task.CompletedTask;
        }
    }

    private sealed class CountingHandler<TEvent> : IApplicationEventHandler<TEvent>
    {
        public int CallCount { get; private set; }
        public TEvent? LastEvent { get; private set; }

        public ValueTask HandleAsync(TEvent applicationEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastEvent = applicationEvent;
            return ValueTask.CompletedTask;
        }
    }
}
