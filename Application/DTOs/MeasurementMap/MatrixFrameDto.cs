namespace Application.DTOs.MeasurementMap;

public sealed record MatrixStatisticsDto(
    double MinValue,
    double MaxValue,
    double AverageValue,
    double StdDev,
    double UniformityMinMax,
    double UniformityMinAverage,
    int ValidCount,
    int InvalidCount)
{
    public double Uniformity => UniformityMinMax;
}

public sealed record MatrixFrameDto(
    DateTime Timestamp,
    int Rows,
    int Columns,
    double[,] Values,
    MatrixStatisticsDto Statistics,
    Guid SourceFrameId = default,
    long SequenceNo = 0);
