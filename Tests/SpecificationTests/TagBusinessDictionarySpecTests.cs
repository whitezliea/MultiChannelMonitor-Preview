using Application.Services;
using Domain.Tags;

namespace Tests.SpecificationTests;

public class TagBusinessDictionarySpecTests
{
    [Fact]
    public void DefaultTagDefinitions_ShouldUseBusinessTagIdNaming()
    {
        var definitions = TagDefinitionCatalog.CreateDefaults();
        var tagIds = definitions.Select(definition => definition.TagId).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(22, tagIds.Count);
        Assert.Contains("MEAS.TEMP.CH01", tagIds);
        Assert.Contains("MEAS.PRESSURE.CH01", tagIds);
        Assert.Contains("MEAS.LIGHT.CH01", tagIds);
        Assert.Contains("MEAS.VOLTAGE.CH01", tagIds);
        Assert.Contains("MEAS.CURRENT.CH01", tagIds);
        Assert.Contains("MEAS.VIBRATION.CH01", tagIds);
        Assert.Contains("MEAS.POWER.CH01", tagIds);
        Assert.Contains("MEAS.LOAD_RATIO.CH01", tagIds);
        Assert.Contains("MATRIX.LIGHT.AVG", tagIds);
        Assert.Contains("MATRIX.LIGHT.MAX", tagIds);
        Assert.Contains("MATRIX.LIGHT.MIN", tagIds);
        Assert.Contains("MATRIX.LIGHT.UNIFORMITY", tagIds);
        Assert.Contains("DEVICE.STATUS", tagIds);
        Assert.Contains("DEVICE.ONLINE", tagIds);
        Assert.Contains("DEVICE.ERROR_CODE", tagIds);
        Assert.Contains("DEVICE.QUALITY", tagIds);
        Assert.Contains("DEVICE.SEQUENCE_NO", tagIds);
    }

    [Fact]
    public void TagSourceMappings_ShouldDescribeRawCodeToTagIdRelationship()
    {
        var expectedMappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TEMP_CH01"] = "MEAS.TEMP.CH01",
            ["PRESSURE_CH01"] = "MEAS.PRESSURE.CH01",
            ["LIGHT_CH01"] = "MEAS.LIGHT.CH01",
            ["VOLTAGE_CH01"] = "MEAS.VOLTAGE.CH01",
            ["CURRENT_CH01"] = "MEAS.CURRENT.CH01",
            ["VIBRATION_CH01"] = "MEAS.VIBRATION.CH01"
        };
        var mappings = TagDefinitionCatalog.CreateSourceMappings();

        Assert.Equal(22, mappings.Count);
        Assert.All(expectedMappings, pair =>
        {
            var mapping = mappings.Single(item => item.SourceType == SourceType.Channel && item.SourceCode == pair.Key);

            Assert.Equal(pair.Value, mapping.TagId);
        });
    }
}
