using Domain.Rules;

namespace Domain.Measurements;

public sealed record MatrixStatistics(
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

public sealed record MatrixFrame(
    Guid FrameId,
    DateTime Timestamp,
    int Rows,
    int Columns,
    double[,] Values,
    Guid SourceFrameId = default,
    long SequenceNo = 0)
{
    public MatrixStatistics CalculateStatistics()
    {
        ArgumentNullException.ThrowIfNull(Values);

        if (Rows != Values.GetLength(0) || Columns != Values.GetLength(1))
        {
            throw new InvalidOperationException(
                $"Matrix dimensions {Rows}x{Columns} do not match values dimensions " +
                $"{Values.GetLength(0)}x{Values.GetLength(1)}.");
        }

        return MatrixStatisticsCalculator.Calculate(Values);
    }
}
