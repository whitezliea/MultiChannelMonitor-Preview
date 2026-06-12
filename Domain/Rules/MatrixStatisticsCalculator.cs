using Domain.Measurements;

namespace Domain.Rules;

public static class MatrixStatisticsCalculator
{
    private const double Epsilon = 1e-9;

    public static MatrixStatistics Calculate(double[,] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        var mean = 0d;
        var sumOfSquaredDifferences = 0d;
        var validCount = 0;
        var invalidCount = 0;

        foreach (var value in values)
        {
            if (!double.IsFinite(value))
            {
                invalidCount++;
                continue;
            }

            min = Math.Min(min, value);
            max = Math.Max(max, value);

            validCount++;
            var delta = value - mean;
            mean += delta / validCount;
            var deltaFromUpdatedMean = value - mean;
            sumOfSquaredDifferences += delta * deltaFromUpdatedMean;
        }

        if (validCount == 0)
        {
            return new MatrixStatistics(
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                0,
                invalidCount);
        }

        var variance = sumOfSquaredDifferences / validCount;
        var stdDev = Math.Sqrt(Math.Max(0d, variance));
        var uniformityMinMax = Math.Abs(max) < Epsilon ? double.NaN : min / max;
        var uniformityMinAverage = Math.Abs(mean) < Epsilon ? double.NaN : min / mean;

        return new MatrixStatistics(
            min,
            max,
            mean,
            stdDev,
            uniformityMinMax,
            uniformityMinAverage,
            validCount,
            invalidCount);
    }
}
