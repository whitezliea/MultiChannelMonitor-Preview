using Application.DTOs.Charts;
using Domain.Tags;

namespace Application.Services.Trend;

public static class TrendStatisticsCalculator
{
    public static TrendStatisticsDto Calculate(IReadOnlyList<TrendPointDto> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var validCount = 0;
        var mean = 0d;
        var sumOfSquaredDifferences = 0d;
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        double? last = null;

        foreach (var point in points)
        {
            if (point.Quality != TagQuality.Good || !double.IsFinite(point.Value))
            {
                continue;
            }

            validCount++;
            last = point.Value;
            minimum = Math.Min(minimum, point.Value);
            maximum = Math.Max(maximum, point.Value);

            var delta = point.Value - mean;
            mean += delta / validCount;
            var deltaFromUpdatedMean = point.Value - mean;
            sumOfSquaredDifferences += delta * deltaFromUpdatedMean;
        }

        if (validCount == 0)
        {
            return new TrendStatisticsDto(
                null,
                null,
                null,
                null,
                null,
                points.Count,
                0);
        }

        var variance = Math.Max(0d, sumOfSquaredDifferences / validCount);
        return new TrendStatisticsDto(
            last,
            minimum,
            maximum,
            mean,
            Math.Sqrt(variance),
            points.Count,
            validCount);
    }
}
