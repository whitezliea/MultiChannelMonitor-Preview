using Application.Pipelines;
using Application.Services;
using Domain.Tags;
using Simulator.Generators;
using Simulator.Scenarios;

namespace Tests.SpecificationTests;

public class DataCleanPipelineMappingSpecTests
{
    [Fact]
    public void Clean_ShouldMapRawChannelCodesToBusinessTagIds()
    {
        var start = DateTime.UtcNow;
        var generator = new FakeDataGenerator("MCMD-001", new NormalScenario(), start);
        var pipeline = new DataCleanPipeline(TagDefinitionCatalog.CreateDefaults());

        var frame = generator.NextFrame(start.AddSeconds(1));
        var tags = pipeline.CleanToCleanedValues(frame);
        var tagIds = tags.Select(tag => tag.TagId).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("MEAS.TEMP.CH01", tagIds);
        Assert.Contains("MEAS.PRESSURE.CH01", tagIds);
        Assert.Contains("MEAS.LIGHT.CH01", tagIds);
        Assert.Contains("MEAS.VOLTAGE.CH01", tagIds);
        Assert.Contains("MEAS.CURRENT.CH01", tagIds);
        Assert.Contains("MEAS.VIBRATION.CH01", tagIds);
    }

    [Fact]
    public void Clean_ShouldCreateDerivedPowerTagFromVoltageAndCurrent()
    {
        var start = DateTime.UtcNow;
        var generator = new FakeDataGenerator("MCMD-001", new NormalScenario(), start);
        var pipeline = new DataCleanPipeline(TagDefinitionCatalog.CreateDefaults());

        var frame = generator.NextFrame(start.AddSeconds(1));
        var tags = pipeline.CleanToCleanedValues(frame);

        var voltage = tags.Single(tag => tag.TagId == "MEAS.VOLTAGE.CH01").NumericValue;
        var current = tags.Single(tag => tag.TagId == "MEAS.CURRENT.CH01").NumericValue;
        var power = tags.Single(tag => tag.TagId == "MEAS.POWER.CH01");

        Assert.Equal(voltage!.Value * current!.Value, power.NumericValue!.Value, precision: 3);
        Assert.Equal(TagQuality.Good, power.Quality);
    }

    [Fact]
    public void Clean_ShouldCreateMatrixStatisticTags()
    {
        var start = DateTime.UtcNow;
        var generator = new FakeDataGenerator("MCMD-001", new NormalScenario(), start);
        var pipeline = new DataCleanPipeline(TagDefinitionCatalog.CreateDefaults());

        var frame = generator.NextFrame(start.AddSeconds(1));
        var tags = pipeline.CleanToCleanedValues(frame);
        var tagIds = tags.Select(tag => tag.TagId).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("MATRIX.LIGHT.AVG", tagIds);
        Assert.Contains("MATRIX.LIGHT.MAX", tagIds);
        Assert.Contains("MATRIX.LIGHT.MIN", tagIds);
        Assert.Contains("MATRIX.LIGHT.UNIFORMITY", tagIds);
        Assert.Contains("MATRIX.LIGHT.ABNORMAL_COUNT", tagIds);
        Assert.Contains("MATRIX.LIGHT.HOTSPOT_ROW", tagIds);
        Assert.Contains("MATRIX.LIGHT.HOTSPOT_COL", tagIds);
    }

    [Fact]
    public void Clean_ShouldCreateDeviceStatusTags()
    {
        var start = DateTime.UtcNow;
        var generator = new FakeDataGenerator("MCMD-001", new OfflineScenario(), start);
        var pipeline = new DataCleanPipeline(TagDefinitionCatalog.CreateDefaults());

        var frame = generator.NextFrame(start.AddSeconds(1));
        var tags = pipeline.CleanToCleanedValues(frame);
        var tagIds = tags.Select(tag => tag.TagId).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("DEVICE.STATUS", tagIds);
        Assert.Contains("DEVICE.ONLINE", tagIds);
        Assert.Contains("DEVICE.ERROR_CODE", tagIds);
        Assert.Contains("DEVICE.QUALITY", tagIds);
        Assert.Contains("DEVICE.SEQUENCE_NO", tagIds);
    }
}
