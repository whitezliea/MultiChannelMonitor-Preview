using Domain.Tags;

namespace Tests.SpecificationTests;

public class RuntimeStateAndPersistenceSpecTests
{
    [Fact]
    public void TagRuntimeState_ShouldSeparateCleanedValueFromRuntimeAlarmState()
    {
        var state = new TagRuntimeState(
            "MEAS.TEMP.CH01",
            "温度 CH01",
            TagCategory.Measurement,
            25.4,
            null,
            null,
            "℃",
            TagDataType.Double,
            TagQuality.Good,
            TagAlarmState.Normal,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            1,
            DateTimeOffset.UtcNow);

        Assert.Equal("MEAS.TEMP.CH01", state.TagId);
        Assert.Equal(TagAlarmState.Normal, state.AlarmState);
        Assert.Null(state.TextValue);
        Assert.Null(state.BoolValue);
    }

    [Fact]
    public void OutOfRangeQuality_ShouldHaveInvalidAlarmState()
    {
        var quality = TagQuality.OutOfRange;
        var expectedAlarmState = TagAlarmState.Invalid;

        Assert.Equal(TagQuality.OutOfRange, quality);
        Assert.Equal(TagAlarmState.Invalid, expectedAlarmState);
    }

    [Fact]
    public void CleanedTagValue_ShouldKeepRuntimeValueAndSourceMetadata()
    {
        var value = new CleanedTagValue(
            "MEAS.TEMP.CH01",
            25.4,
            null,
            null,
            TagDataType.Double,
            "℃",
            DateTimeOffset.UtcNow,
            TagQuality.Good,
            "MCMD-001",
            "TEMP_CH01",
            Guid.NewGuid(),
            1,
            null);

        Assert.Equal(25.4, value.NumericValue);
        Assert.Equal("TEMP_CH01", value.SourceCode);
        Assert.NotEqual(Guid.Empty, value.SourceFrameId);
    }

    [Fact]
    public void MatrixFrames_ShouldStoreMatrixBodySeparatelyFromStatisticTags()
    {
        var statisticTags = new[]
        {
            "MATRIX.LIGHT.AVG",
            "MATRIX.LIGHT.MAX",
            "MATRIX.LIGHT.MIN",
            "MATRIX.LIGHT.UNIFORMITY"
        };

        Assert.DoesNotContain("MATRIX.P00_00", statisticTags);
        Assert.Contains("MATRIX.LIGHT.AVG", statisticTags);
    }
}
