using Domain.Measurements;

namespace Tests.DomainTests;

public class MatrixFrameTests
{
    [Fact]
    public void CalculateStatistics_ReturnsExpectedValues()
    {
        var values = new double[,] { { 1, 2 }, { 3, 4 } };
        var frame = new MatrixFrame(Guid.NewGuid(), DateTime.UtcNow, 2, 2, values);

        var statistics = frame.CalculateStatistics();

        Assert.Equal(1, statistics.MinValue);
        Assert.Equal(4, statistics.MaxValue);
        Assert.Equal(2.5, statistics.AverageValue);
        Assert.Equal(Math.Sqrt(1.25), statistics.StdDev, precision: 12);
        Assert.Equal(0.25, statistics.UniformityMinMax);
        Assert.Equal(statistics.UniformityMinMax, statistics.Uniformity);
        Assert.Equal(0.4, statistics.UniformityMinAverage);
        Assert.Equal(4, statistics.ValidCount);
        Assert.Equal(0, statistics.InvalidCount);
    }

    [Fact]
    public void CalculateStatistics_IgnoresNonFiniteValues()
    {
        var values = new double[,]
        {
            { 1, double.NaN },
            { double.PositiveInfinity, 3 }
        };
        var frame = new MatrixFrame(Guid.NewGuid(), DateTime.UtcNow, 2, 2, values);

        var statistics = frame.CalculateStatistics();

        Assert.Equal(1, statistics.MinValue);
        Assert.Equal(3, statistics.MaxValue);
        Assert.Equal(2, statistics.AverageValue);
        Assert.Equal(1, statistics.StdDev);
        Assert.Equal(2, statistics.ValidCount);
        Assert.Equal(2, statistics.InvalidCount);
    }

    [Fact]
    public void CalculateStatistics_ReturnsNaNStatisticsWhenAllValuesAreInvalid()
    {
        var values = new double[,] { { double.NaN, double.NegativeInfinity } };
        var frame = new MatrixFrame(Guid.NewGuid(), DateTime.UtcNow, 1, 2, values);

        var statistics = frame.CalculateStatistics();

        Assert.True(double.IsNaN(statistics.MinValue));
        Assert.True(double.IsNaN(statistics.MaxValue));
        Assert.True(double.IsNaN(statistics.AverageValue));
        Assert.True(double.IsNaN(statistics.StdDev));
        Assert.True(double.IsNaN(statistics.UniformityMinMax));
        Assert.True(double.IsNaN(statistics.UniformityMinAverage));
        Assert.Equal(0, statistics.ValidCount);
        Assert.Equal(2, statistics.InvalidCount);
    }

    [Fact]
    public void CalculateStatistics_ReturnsZeroDeviationForConstantMatrix()
    {
        var values = new double[,] { { 5, 5 }, { 5, 5 } };
        var frame = new MatrixFrame(Guid.NewGuid(), DateTime.UtcNow, 2, 2, values);

        var statistics = frame.CalculateStatistics();

        Assert.Equal(0, statistics.StdDev);
        Assert.Equal(1, statistics.UniformityMinMax);
        Assert.Equal(1, statistics.UniformityMinAverage);
    }

    [Fact]
    public void CalculateStatistics_ReturnsNaNUniformityWhenDenominatorIsZero()
    {
        var values = new double[,] { { -1, 0, 1 } };
        var frame = new MatrixFrame(Guid.NewGuid(), DateTime.UtcNow, 1, 3, values);

        var statistics = frame.CalculateStatistics();

        Assert.Equal(-1, statistics.MinValue);
        Assert.Equal(1, statistics.MaxValue);
        Assert.True(double.IsNaN(statistics.UniformityMinAverage));
    }

    [Fact]
    public void CalculateStatistics_RejectsMismatchedDimensions()
    {
        var frame = new MatrixFrame(Guid.NewGuid(), DateTime.UtcNow, 2, 2, new double[1, 2]);

        var exception = Assert.Throws<InvalidOperationException>(() => frame.CalculateStatistics());

        Assert.Contains("2x2", exception.Message);
        Assert.Contains("1x2", exception.Message);
    }
}
