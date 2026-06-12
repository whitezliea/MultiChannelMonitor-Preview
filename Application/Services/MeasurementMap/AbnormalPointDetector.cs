using Application.DTOs.MeasurementMap;
using Domain.Measurements;

namespace Application.Services.MeasurementMap;

public sealed class AbnormalPointDetector
{
    private const double Epsilon = 1e-9;

    public IReadOnlyList<AbnormalMatrixPointDto> Detect(
        MatrixFrame frame,
        MatrixStatistics statistics,
        MatrixAbnormalDetectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(statistics);
        options ??= MatrixAbnormalDetectionOptions.Default;

        if (frame.Rows != frame.Values.GetLength(0) || frame.Columns != frame.Values.GetLength(1))
        {
            throw new InvalidOperationException("Matrix frame dimensions do not match the values array.");
        }

        ValidateOptions(options);

        var result = new List<AbnormalMatrixPointDto>();

        for (var row = 0; row < frame.Rows; row++)
        {
            for (var column = 0; column < frame.Columns; column++)
            {
                var value = frame.Values[row, column];

                if (!double.IsFinite(value))
                {
                    result.Add(new AbnormalMatrixPointDto(
                        row, column, value, MatrixAbnormalType.InvalidValue, MatrixSeverity.Alarm,
                        "Invalid sensor value."));
                    continue;
                }

                if (options.HighLimit.HasValue && value > options.HighLimit.Value)
                {
                    result.Add(new AbnormalMatrixPointDto(
                        row, column, value, MatrixAbnormalType.HighLimit, MatrixSeverity.Alarm,
                        "Value exceeds the high engineering limit."));
                    continue;
                }

                if (options.LowLimit.HasValue && value < options.LowLimit.Value)
                {
                    result.Add(new AbnormalMatrixPointDto(
                        row, column, value, MatrixAbnormalType.LowLimit, MatrixSeverity.Alarm,
                        "Value is below the low engineering limit."));
                    continue;
                }

                if (double.IsFinite(statistics.StdDev) && statistics.StdDev > Epsilon)
                {
                    var zScore = (value - statistics.AverageValue) / statistics.StdDev;
                    if (zScore >= options.ZScoreThreshold)
                    {
                        result.Add(new AbnormalMatrixPointDto(
                            row, column, value, MatrixAbnormalType.StatisticalHotspot, MatrixSeverity.Warning,
                            "Statistical hotspot detected."));
                        continue;
                    }

                    if (zScore <= -options.ZScoreThreshold)
                    {
                        result.Add(new AbnormalMatrixPointDto(
                            row, column, value, MatrixAbnormalType.StatisticalColdspot, MatrixSeverity.Warning,
                            "Statistical coldspot detected."));
                        continue;
                    }
                }

                if (!TryGetLocalMedian(frame.Values, frame.Rows, frame.Columns, row, column, out var localMedian))
                {
                    continue;
                }

                var localDelta = value - localMedian;
                var localRelativeDelta = Math.Abs(localMedian) < Epsilon
                    ? 0d
                    : localDelta / Math.Abs(localMedian);
                var localHotspotByStdDev = statistics.StdDev > Epsilon &&
                    localDelta > options.LocalStdDevMultiplier * statistics.StdDev;
                var localColdspotByStdDev = statistics.StdDev > Epsilon &&
                    localDelta < -options.LocalStdDevMultiplier * statistics.StdDev;

                if (localHotspotByStdDev || localRelativeDelta > options.LocalRelativeThreshold)
                {
                    result.Add(new AbnormalMatrixPointDto(
                        row, column, value, MatrixAbnormalType.LocalHotspot, MatrixSeverity.Warning,
                        "Local value is significantly higher than neighboring points."));
                    continue;
                }

                if (localColdspotByStdDev || localRelativeDelta < -options.LocalRelativeThreshold)
                {
                    result.Add(new AbnormalMatrixPointDto(
                        row, column, value, MatrixAbnormalType.LocalColdspot, MatrixSeverity.Warning,
                        "Local value is significantly lower than neighboring points."));
                }
            }
        }

        return result;
    }

    private static bool TryGetLocalMedian(
        double[,] values,
        int rows,
        int columns,
        int row,
        int column,
        out double median)
    {
        var neighbors = new List<double>(8);

        for (var currentRow = Math.Max(0, row - 1); currentRow <= Math.Min(rows - 1, row + 1); currentRow++)
        {
            for (var currentColumn = Math.Max(0, column - 1); currentColumn <= Math.Min(columns - 1, column + 1); currentColumn++)
            {
                if (currentRow == row && currentColumn == column)
                {
                    continue;
                }

                var value = values[currentRow, currentColumn];
                if (double.IsFinite(value))
                {
                    neighbors.Add(value);
                }
            }
        }

        if (neighbors.Count == 0)
        {
            median = double.NaN;
            return false;
        }

        neighbors.Sort();
        var middle = neighbors.Count / 2;
        median = neighbors.Count % 2 == 1
            ? neighbors[middle]
            : (neighbors[middle - 1] + neighbors[middle]) / 2d;
        return true;
    }

    private static void ValidateOptions(MatrixAbnormalDetectionOptions options)
    {
        if ((options.HighLimit.HasValue && !double.IsFinite(options.HighLimit.Value)) ||
            (options.LowLimit.HasValue && !double.IsFinite(options.LowLimit.Value)))
        {
            throw new ArgumentException("Engineering limits must be finite when specified.", nameof(options));
        }

        if (options.HighLimit.HasValue && options.LowLimit.HasValue && options.LowLimit.Value > options.HighLimit.Value)
        {
            throw new ArgumentException("LowLimit cannot be greater than HighLimit.", nameof(options));
        }

        if (!double.IsFinite(options.ZScoreThreshold) ||
            !double.IsFinite(options.LocalStdDevMultiplier) ||
            !double.IsFinite(options.LocalRelativeThreshold) ||
            options.ZScoreThreshold <= 0 ||
            options.LocalStdDevMultiplier <= 0 ||
            options.LocalRelativeThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Detection thresholds must be greater than zero.");
        }
    }
}
