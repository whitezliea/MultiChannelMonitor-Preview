using Application.DTOs.MeasurementMap;

namespace Application.Services.MeasurementMap;

public sealed class MatrixQualityEvaluator
{
    public MatrixQualityState Evaluate(
        MatrixStatisticsDto statistics,
        IReadOnlyList<AbnormalMatrixPointDto> abnormalPoints)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(abnormalPoints);

        if (statistics.InvalidCount > 0 || abnormalPoints.Any(point => point.Severity == MatrixSeverity.Alarm))
        {
            return MatrixQualityState.Alarm;
        }

        if (abnormalPoints.Any(point => point.Severity == MatrixSeverity.Warning))
        {
            return MatrixQualityState.Warning;
        }

        if (double.IsFinite(statistics.UniformityMinMax))
        {
            if (statistics.UniformityMinMax < 0.70d)
            {
                return MatrixQualityState.Warning;
            }

            if (statistics.UniformityMinMax < 0.80d)
            {
                return MatrixQualityState.Attention;
            }
        }

        return MatrixQualityState.Good;
    }
}
