using Application.DTOs.Charts;
using Application.Services.Trend;

namespace Tests.ApplicationTests;

public class TrendStatisticsCalculatorTests
{
    [Fact]
    public void Calculate_ReturnsEmptyStatisticsForNoValidPoints()
    {
        var points = new[]
        {
            new TrendPointDto(DateTime.UtcNow, double.NaN),
            new TrendPointDto(DateTime.UtcNow, double.PositiveInfinity)
        };

        var statistics = TrendStatisticsCalculator.Calculate(points);

        Assert.Equal(2, statistics.TotalCount);
        Assert.Equal(0, statistics.ValidCount);
        Assert.Null(statistics.Last);
        Assert.Null(statistics.Minimum);
        Assert.Null(statistics.Maximum);
        Assert.Null(statistics.Average);
        Assert.Null(statistics.StdDev);
    }

    [Fact]
    public void Calculate_UsesFinitePointsAndPopulationStandardDeviation()
    {
        var timestamp = DateTime.UtcNow;
        var points = new[]
        {
            new TrendPointDto(timestamp, 1),
            new TrendPointDto(timestamp.AddSeconds(1), double.NaN),
            new TrendPointDto(timestamp.AddSeconds(2), 2),
            new TrendPointDto(timestamp.AddSeconds(3), 3)
        };

        var statistics = TrendStatisticsCalculator.Calculate(points);

        Assert.Equal(4, statistics.TotalCount);
        Assert.Equal(3, statistics.ValidCount);
        Assert.Equal(3, statistics.Last);
        Assert.Equal(1, statistics.Minimum);
        Assert.Equal(3, statistics.Maximum);
        Assert.Equal(2, statistics.Average);
        Assert.Equal(Math.Sqrt(2d / 3d), statistics.StdDev!.Value, 12);
    }

    [Fact]
    public void Calculate_ExcludesNonGoodPoints()
    {
        var timestamp = DateTime.UtcNow;
        var points = new[]
        {
            new TrendPointDto(timestamp, 10),
            new TrendPointDto(
                timestamp.AddSeconds(1),
                1000,
                Domain.Tags.TagQuality.Offline)
        };

        var statistics = TrendStatisticsCalculator.Calculate(points);

        Assert.Equal(2, statistics.TotalCount);
        Assert.Equal(1, statistics.ValidCount);
        Assert.Equal(10, statistics.Last);
        Assert.Equal(10, statistics.Average);
    }
}
