using Application.DTOs.MeasurementMap;

namespace Application.Services.MeasurementMap;

public sealed class MatrixScaleService
{
    private const double Epsilon = 1e-9;

    public ScaleRangeDto Resolve(MatrixStatisticsDto statistics, MatrixDisplayOptionsDto options)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(options);

        return options.ScaleMode switch
        {
            MatrixScaleMode.FixedEngineeringRange => ResolveFixed(options),
            _ => new ScaleRangeDto(statistics.MinValue, statistics.MaxValue)
        };
    }

    public double Normalize(double value, ScaleRangeDto scaleRange)
    {
        ArgumentNullException.ThrowIfNull(scaleRange);

        if (!double.IsFinite(value) || !double.IsFinite(scaleRange.MinValue) || !double.IsFinite(scaleRange.MaxValue))
        {
            return double.NaN;
        }

        if (Math.Abs(scaleRange.Range) < Epsilon)
        {
            return 0.5d;
        }

        return Math.Clamp((value - scaleRange.MinValue) / scaleRange.Range, 0d, 1d);
    }

    private static ScaleRangeDto ResolveFixed(MatrixDisplayOptionsDto options)
    {
        if (!options.FixedMin.HasValue || !options.FixedMax.HasValue)
        {
            throw new InvalidOperationException("Fixed engineering range requires FixedMin and FixedMax.");
        }

        if (!double.IsFinite(options.FixedMin.Value) || !double.IsFinite(options.FixedMax.Value))
        {
            throw new ArgumentException("Fixed engineering range values must be finite.", nameof(options));
        }

        if (options.FixedMin.Value > options.FixedMax.Value)
        {
            throw new ArgumentException("FixedMin cannot be greater than FixedMax.", nameof(options));
        }

        return new ScaleRangeDto(options.FixedMin.Value, options.FixedMax.Value);
    }
}
