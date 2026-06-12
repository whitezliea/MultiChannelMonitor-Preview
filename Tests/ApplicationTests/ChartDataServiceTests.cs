using Application.Caches;
using Application.Configuration;
using Application.DTOs.Charts;
using Application.Services;
using Domain.Tags;
using Tests.Support;

namespace Tests.ApplicationTests;

public class ChartDataServiceTests
{
    [Fact]
    public void GetTrendSeries_ReturnsLastRequestedPoints()
    {
        var tagService = new TagService(new TagCache(trendBufferCapacity: 10), CreateClock());
        tagService.UpdateTags([
            CreateState("MEAS.TEMP.CH01", 1, 1),
            CreateState("MEAS.TEMP.CH01", 2, 2),
            CreateState("MEAS.TEMP.CH01", 3, 3)
        ]);
        var service = new ChartDataService(tagService);

        var series = service.GetTrendSeries("MEAS.TEMP.CH01", 2);

        Assert.Equal("MEAS.TEMP.CH01", series.TagId);
        Assert.Equal([2d, 3d], series.Points.Select(point => point.Value));
        Assert.Equal(2, series.RequestedPointCount);
        Assert.True(series.IsWindowComplete);
    }

    [Fact]
    public void GetTrendSeries_ReturnsEmptyForUnknownTagOrNonPositiveCount()
    {
        var service = new ChartDataService(new TagService(new TagCache(trendBufferCapacity: 10), CreateClock()));

        var unknown = service.GetTrendSeries("UNKNOWN", 10);
        var nonPositive = service.GetTrendSeries("MEAS.TEMP.CH01", 0);

        Assert.Empty(unknown.Points);
        Assert.Equal(10, unknown.RequestedPointCount);
        Assert.False(unknown.IsWindowComplete);
        Assert.Empty(nonPositive.Points);
        Assert.Equal(0, nonPositive.RequestedPointCount);
        Assert.False(nonPositive.IsWindowComplete);
    }

    [Fact]
    public void DefaultCache_RetainsCompleteThirtyMinuteWindow()
    {
        var options = new MonitorRuntimeOptions();
        var tagService = new TagService(new TagCache(options.TrendBufferCapacity), CreateClock());
        tagService.UpdateTags(
            Enumerable.Range(1, options.TrendBufferCapacity + 1)
                .Select(sequenceNo => CreateState("MEAS.TEMP.CH01", sequenceNo, sequenceNo))
                .ToArray());
        var service = new ChartDataService(tagService);

        var series = service.GetTrendSeries(
            "MEAS.TEMP.CH01",
            options.GetTrendPointCount(TimeSpan.FromMinutes(30)));

        Assert.Equal(3600, series.Points.Count);
        Assert.Equal(2, series.Points[0].Value);
        Assert.Equal(3601, series.Points[^1].Value);
        Assert.True(series.IsWindowComplete);
    }

    [Fact]
    public void BuildTrendSnapshot_ClipsByUtcWindowAndBuildsCurrentState()
    {
        var capturedAt = new DateTime(2026, 6, 12, 4, 0, 0, DateTimeKind.Utc);
        var tagService = new TagService(new TagCache(trendBufferCapacity: 10), new TestClock(capturedAt));
        var sourceFrameId = Guid.NewGuid();
        tagService.UpdateTags([
            CreateState("MEAS.TEMP.CH01", 10, 1, capturedAt.AddMinutes(-2), Guid.NewGuid()),
            CreateState("MEAS.TEMP.CH01", 20, 2, capturedAt.AddSeconds(-30), Guid.NewGuid()),
            CreateState(
                "MEAS.TEMP.CH01",
                30,
                3,
                capturedAt,
                sourceFrameId,
                TagQuality.DeviceError,
                TagAlarmState.AlarmHigh)
        ]);
        var definition = new TagDefinition(
            "MEAS.TEMP.CH01",
            "Temperature",
            TagCategory.Measurement,
            "C",
            -20,
            120);
        var configurations = new TagRuntimeConfigurationStore([
            TagRuntimeConfiguration.FromDefinition(definition)
        ]);
        var service = new ChartDataService(
            tagService,
            new Dictionary<string, TagDefinition> { [definition.TagId] = definition },
            configurations);

        var snapshot = service.BuildTrendSnapshot(
            tagService.GetSnapshot(),
            definition.TagId,
            TimeSpan.FromMinutes(1),
            120,
            capturedAt);

        Assert.Equal([20d, 30d], snapshot.Series.Points.Select(point => point.Value));
        Assert.Equal(TimeSpan.FromMinutes(1), snapshot.Window);
        Assert.Equal(120, snapshot.ExpectedPointCount);
        Assert.Equal("Temperature", snapshot.Metadata.DisplayName);
        Assert.Equal("C", snapshot.Metadata.Unit);
        Assert.True(snapshot.Metadata.IsKnownTag);
        Assert.Equal(30, snapshot.CurrentValue);
        Assert.Equal(TagQuality.DeviceError, snapshot.CurrentQuality);
        Assert.Equal(TagAlarmState.AlarmHigh, snapshot.CurrentAlarmState);
        Assert.Equal(capturedAt, snapshot.CurrentTimestamp);
        Assert.Equal(sourceFrameId, snapshot.SourceFrameId);
        Assert.Equal(3, snapshot.SequenceNo);
        Assert.Equal(TagQuality.Good, snapshot.Series.Points[0].Quality);
        Assert.Equal(TagQuality.DeviceError, snapshot.Series.Points[1].Quality);
        Assert.Equal(1, snapshot.Statistics.ValidCount);
        Assert.Equal(20, snapshot.Statistics.Average);
        Assert.Equal(TrendDiagnosisState.InsufficientData, snapshot.Diagnosis.State);
    }

    [Fact]
    public void BuildTrendSnapshot_UsesCurrentRuntimeThresholdsAndRevision()
    {
        var capturedAt = new DateTime(2026, 6, 12, 4, 0, 0, DateTimeKind.Utc);
        var definition = new TagDefinition(
            "MEAS.TEMP.CH01",
            "Temperature",
            TagCategory.Measurement,
            "C",
            WarningHigh: 60,
            AlarmHigh: 80);
        var store = new TagRuntimeConfigurationStore([
            TagRuntimeConfiguration.FromDefinition(definition)
        ]);
        store.Replace([
            new TagRuntimeConfiguration(
                definition.TagId,
                true,
                5,
                0,
                55,
                75,
                true,
                1000)
        ]);
        var tagService = new TagService(new TagCache(10), new TestClock(capturedAt));
        tagService.UpdateTags([
            CreateState(definition.TagId, 25, 1, capturedAt, Guid.NewGuid())
        ]);
        var service = new ChartDataService(
            tagService,
            new Dictionary<string, TagDefinition> { [definition.TagId] = definition },
            store);

        var snapshot = service.BuildTrendSnapshot(
            tagService.GetSnapshot(),
            definition.TagId,
            TimeSpan.FromMinutes(1),
            120,
            capturedAt);

        Assert.Equal(1, snapshot.ConfigurationRevision);
        Assert.Collection(
            snapshot.Thresholds,
            threshold =>
            {
                Assert.Equal(TrendThresholdType.AlarmLow, threshold.Type);
                Assert.Equal(0, threshold.Value);
            },
            threshold =>
            {
                Assert.Equal(TrendThresholdType.WarningLow, threshold.Type);
                Assert.Equal(5, threshold.Value);
            },
            threshold =>
            {
                Assert.Equal(TrendThresholdType.WarningHigh, threshold.Type);
                Assert.Equal(55, threshold.Value);
            },
            threshold =>
            {
                Assert.Equal(TrendThresholdType.AlarmHigh, threshold.Type);
                Assert.Equal(75, threshold.Value);
            });
    }

    [Fact]
    public void BuildTrendSnapshot_HidesThresholdsWhenRuntimeAlarmIsDisabled()
    {
        var capturedAt = new DateTime(2026, 6, 12, 4, 0, 0, DateTimeKind.Utc);
        var definition = new TagDefinition(
            "MEAS.TEMP.CH01",
            "Temperature",
            TagCategory.Measurement,
            "C",
            WarningHigh: 60,
            AlarmHigh: 80);
        var store = new TagRuntimeConfigurationStore([
            new TagRuntimeConfiguration(
                definition.TagId,
                false,
                null,
                null,
                60,
                80,
                true,
                1000)
        ]);
        var tagService = new TagService(new TagCache(10), new TestClock(capturedAt));
        var service = new ChartDataService(
            tagService,
            new Dictionary<string, TagDefinition> { [definition.TagId] = definition },
            store);

        var snapshot = service.BuildTrendSnapshot(
            tagService.GetSnapshot(),
            definition.TagId,
            TimeSpan.FromMinutes(1),
            120,
            capturedAt);

        Assert.Empty(snapshot.Thresholds);
        Assert.Equal(TrendDiagnosisState.InsufficientData, snapshot.Diagnosis.State);
    }

    private static TagRuntimeState CreateState(string tagId, double value, long sequenceNo)
    {
        var timestamp = DateTimeOffset.UtcNow.AddSeconds(sequenceNo);
        return CreateState(tagId, value, sequenceNo, timestamp.UtcDateTime, Guid.NewGuid());
    }

    private static TagRuntimeState CreateState(
        string tagId,
        double value,
        long sequenceNo,
        DateTime timestampUtc,
        Guid sourceFrameId,
        TagQuality quality = TagQuality.Good,
        TagAlarmState alarmState = TagAlarmState.Normal)
    {
        var timestamp = new DateTimeOffset(timestampUtc);
        return new TagRuntimeState(
            tagId,
            tagId,
            TagCategory.Measurement,
            value,
            null,
            null,
            null,
            TagDataType.Double,
            quality,
            alarmState,
            timestamp,
            sourceFrameId,
            sequenceNo,
            timestamp);
    }

    private static TestClock CreateClock() => new(DateTime.UtcNow);
}
