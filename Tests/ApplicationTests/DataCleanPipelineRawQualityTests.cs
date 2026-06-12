using Application.Pipelines;
using Application.Services;
using Domain.Tags;
using Simulator.Generators;
using Simulator.Scenarios;

namespace Tests.ApplicationTests;

public class DataCleanPipelineRawQualityTests
{
    [Fact]
    public void Clean_PreservesSingleChannelDeviceError()
    {
        var start = DateTime.UtcNow;
        var generator = new FakeDataGenerator("MCMD-TEST", new DemoScenario(), start);
        var pipeline = new DataCleanPipeline(TagDefinitionCatalog.CreateDefaults());

        var frame = generator.NextFrame(start.AddSeconds(66));
        var cleanedValues = pipeline.CleanToCleanedValues(frame);
        var states = new AlarmService(TagDefinitionCatalog.CreateDefaults()).Evaluate(cleanedValues, DateTimeOffset.UtcNow);
        var light = states.Single(tag => tag.TagId == "MEAS.LIGHT.CH01");

        Assert.Equal(TagQuality.DeviceError, light.Quality);
        Assert.Equal(TagAlarmState.Invalid, light.AlarmState);
        Assert.Null(light.NumericValue);
    }

    [Fact]
    public void Clean_MapsOfflineChannelsToOfflineTags()
    {
        var start = DateTime.UtcNow;
        var generator = new FakeDataGenerator("MCMD-TEST", new OfflineScenario(), start);
        var pipeline = new DataCleanPipeline(TagDefinitionCatalog.CreateDefaults());

        var frame = generator.NextFrame(start.AddSeconds(1));
        var cleanedValues = pipeline.CleanToCleanedValues(frame);
        var tags = new AlarmService(TagDefinitionCatalog.CreateDefaults()).Evaluate(cleanedValues, DateTimeOffset.UtcNow);

        Assert.Contains(tags, tag => tag.Quality == TagQuality.Offline);
        Assert.All(tags.Where(tag => tag.TagId != "MATRIX.LIGHT.AVG" && tag.TagId != "MATRIX.LIGHT.MAX" && tag.TagId != "MATRIX.LIGHT.MIN" && tag.TagId != "MATRIX.LIGHT.UNIFORMITY" && tag.TagId != "MATRIX.LIGHT.ABNORMAL_COUNT" && tag.TagId != "MATRIX.LIGHT.HOTSPOT_ROW" && tag.TagId != "MATRIX.LIGHT.HOTSPOT_COL"), tag =>
            Assert.Equal(TagAlarmState.Offline, tag.AlarmState));
    }
}
