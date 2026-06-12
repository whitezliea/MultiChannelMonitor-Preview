using Domain.Measurements;

namespace Domain.Rules;

public static class MatrixStatisticsRule
{
    public static MatrixStatistics Calculate(MatrixFrame frame) => frame.CalculateStatistics();
}
