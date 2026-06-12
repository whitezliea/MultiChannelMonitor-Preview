using Application.Caches;
using Application.Pipelines;
using Application.Services;
using Domain.Tags;
using Simulator.Generators;

namespace Tests.ApplicationTests;

public class DataPipelineTests
{
    [Fact]
    public void CleanPipeline_MapsRawFrameToStandardTags()
    {
        var generator = new FakeDataGenerator();
        var frame = generator.NextFrame(DateTime.UtcNow);
        var pipeline = new DataCleanPipeline(TagDefinitionCatalog.CreateDefaults());

        var tags = pipeline.CleanToCleanedValues(frame);

        Assert.Contains(tags, tag => tag.TagId == "MEAS.TEMP.CH01");
        Assert.Contains(tags, tag => tag.TagId == "MEAS.POWER.CH01");
        Assert.Contains(tags, tag => tag.TagId == "MATRIX.LIGHT.AVG");
    }

    [Fact]
    public void TagCache_StoresLatestSnapshotAndTrendBuffer()
    {
        var cache = new TagCache(trendBufferCapacity: 2);
        cache.Update([
            CreateState("MEAS.TEMP.CH01", 25, DateTimeOffset.UtcNow, 1),
            CreateState("MEAS.TEMP.CH01", 26, DateTimeOffset.UtcNow.AddSeconds(1), 2),
            CreateState("MEAS.TEMP.CH01", 27, DateTimeOffset.UtcNow.AddSeconds(2), 3)
        ]);

        var snapshot = cache.GetSnapshot();

        Assert.Single(snapshot.CurrentValues);
        Assert.Equal(27, snapshot.CurrentValues[0].NumericValue);
        Assert.Equal(2, snapshot.RecentBuffers["MEAS.TEMP.CH01"].Count);
    }

    [Fact]
    public void TagCache_RejectsNonPositiveTrendCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TagCache(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TagCache(-1));
    }

    [Fact]
    public void AlarmService_CreatesActiveAlarmForBadTagState()
    {
        var service = new AlarmService(TagDefinitionCatalog.CreateDefaults());
        var cleaned = new CleanedTagValue(
            "MEAS.VIBRATION.CH01",
            6,
            null,
            null,
            TagDataType.Double,
            "mm/s",
            DateTimeOffset.UtcNow,
            TagQuality.Good,
            "MCMD-001",
            "VIBRATION_CH01",
            Guid.NewGuid(),
            1,
            null);

        var states = service.Evaluate([cleaned], DateTimeOffset.UtcNow);

        var alarms = service.GetActiveAlarms();

        Assert.Equal(TagAlarmState.AlarmHigh, states[0].AlarmState);
        Assert.Single(alarms);
        Assert.Equal("MEAS.VIBRATION.CH01", alarms[0].TagId);
    }

    private static TagRuntimeState CreateState(string tagId, double value, DateTimeOffset timestamp, long sequenceNo) =>
        new(
            tagId,
            tagId,
            TagCategory.Measurement,
            value,
            null,
            null,
            null,
            TagDataType.Double,
            TagQuality.Good,
            TagAlarmState.Normal,
            timestamp,
            Guid.NewGuid(),
            sequenceNo,
            timestamp);
}
