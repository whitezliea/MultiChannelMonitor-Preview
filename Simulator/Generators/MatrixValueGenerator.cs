using Domain.Measurements;
using Simulator.Models;
using Simulator.Noise;
using Simulator.Scenarios;

namespace Simulator.Generators;

public sealed class MatrixValueGenerator
{
    private readonly Random _random;

    public MatrixValueGenerator(int seed = 20260608)
    {
        _random = new Random(seed);
    }

    public MatrixFrame Generate(MatrixSimulationSpec spec, DateTime timestamp, TimeSpan elapsed, MatrixEffect effect)
    {
        var values = new double[spec.Rows, spec.Columns];
        var centerRow = (spec.Rows - 1) / 2.0;
        var centerCol = (spec.Columns - 1) / 2.0;
        var maxDistance = Distance(0, 0, centerRow, centerCol);
        var slowWave = Math.Sin(elapsed.TotalSeconds / 10.0) * 8.0;

        for (var row = 0; row < spec.Rows; row++)
        {
            for (var column = 0; column < spec.Columns; column++)
            {
                var distance = Distance(row, column, centerRow, centerCol);
                var normalizedDistance = distance / maxDistance;
                var centerGain = spec.CenterAmplitude * Math.Exp(-Math.Pow(normalizedDistance, 2) * 2.5);
                var edgeDrop = spec.EdgeDrop * normalizedDistance;
                var value = spec.BaseValue + centerGain - edgeDrop + slowWave + _random.NextGaussian(0, spec.NoiseSigma);

                if (effect.AddHotspot)
                {
                    value += GaussianBump(row, column, effect.HotspotRow, effect.HotspotColumn, effect.HotspotAmplitude, sigma: 1.4);
                }

                if (effect.AddLowRegion)
                {
                    var weight = GaussianWeight(row, column, effect.LowRegionRow, effect.LowRegionColumn, sigma: 1.8);
                    value *= 1.0 - weight * (1.0 - effect.LowRegionScale);
                }

                values[row, column] = Math.Round(value, 3);
            }
        }

        return new MatrixFrame(Guid.NewGuid(), timestamp, spec.Rows, spec.Columns, values);
    }

    private static double Distance(double row, double column, double centerRow, double centerColumn)
    {
        var rowDelta = row - centerRow;
        var columnDelta = column - centerColumn;
        return Math.Sqrt(rowDelta * rowDelta + columnDelta * columnDelta);
    }

    private static double GaussianBump(int row, int column, int centerRow, int centerColumn, double amplitude, double sigma) =>
        amplitude * GaussianWeight(row, column, centerRow, centerColumn, sigma);

    private static double GaussianWeight(int row, int column, int centerRow, int centerColumn, double sigma)
    {
        var rowDelta = row - centerRow;
        var columnDelta = column - centerColumn;
        var distanceSquared = rowDelta * rowDelta + columnDelta * columnDelta;
        return Math.Exp(-distanceSquared / (2.0 * sigma * sigma));
    }
}
