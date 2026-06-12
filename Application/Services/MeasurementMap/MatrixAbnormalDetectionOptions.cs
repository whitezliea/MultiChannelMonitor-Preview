namespace Application.Services.MeasurementMap;

public sealed record MatrixAbnormalDetectionOptions(
    double? HighLimit = 1500d,
    double? LowLimit = 100d,
    double ZScoreThreshold = 2.5d,
    double LocalStdDevMultiplier = 1.8d,
    double LocalRelativeThreshold = 0.12d)
{
    public static MatrixAbnormalDetectionOptions Default { get; } = new();
}
