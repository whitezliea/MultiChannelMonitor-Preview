using Application.Configuration;

namespace Tests.ApplicationTests;

public sealed class MonitorRuntimeOptionsTests
{
    [Fact]
    public void DefaultTrendCapacity_CoversConfiguredThirtyMinuteWindow()
    {
        var options = new MonitorRuntimeOptions();

        Assert.Equal(TimeSpan.FromMinutes(30), options.MaximumTrendWindow);
        Assert.Equal(120, options.GetTrendPointCount(TimeSpan.FromMinutes(1)));
        Assert.Equal(600, options.GetTrendPointCount(TimeSpan.FromMinutes(5)));
        Assert.Equal(3600, options.GetTrendPointCount(TimeSpan.FromMinutes(30)));
        Assert.Equal(3600, options.TrendBufferCapacity);
    }

    [Fact]
    public void TrendCapacity_RecalculatesWhenSamplingIntervalChanges()
    {
        var options = new MonitorRuntimeOptions
        {
            DataGenerateInterval = TimeSpan.FromSeconds(1)
        };

        Assert.Equal(60, options.GetTrendPointCount(TimeSpan.FromMinutes(1)));
        Assert.Equal(1800, options.TrendBufferCapacity);
    }

    [Fact]
    public void TrendPointCount_RoundsUpForNonDivisibleIntervals()
    {
        var options = new MonitorRuntimeOptions
        {
            DataGenerateInterval = TimeSpan.FromMilliseconds(700),
            TrendWindows = [TimeSpan.FromSeconds(2)]
        };

        Assert.Equal(3, options.GetTrendPointCount(TimeSpan.FromSeconds(2)));
        Assert.Equal(3, options.TrendBufferCapacity);
    }

    [Fact]
    public void TrendConfiguration_RejectsInvalidIntervalsAndWindows()
    {
        var invalidInterval = new MonitorRuntimeOptions
        {
            DataGenerateInterval = TimeSpan.Zero
        };
        var options = new MonitorRuntimeOptions();

        Assert.Throws<InvalidOperationException>(() => invalidInterval.GetTrendPointCount(TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.GetTrendPointCount(TimeSpan.Zero));
    }
}
