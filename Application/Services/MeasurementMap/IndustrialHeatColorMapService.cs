using Application.DTOs.MeasurementMap;

namespace Application.Services.MeasurementMap;

public sealed class IndustrialHeatColorMapService
{
    private static readonly (double Stop, RgbColorDto Color)[] Stops =
    [
        (0.00d, new RgbColorDto(20, 36, 78)),
        (0.25d, new RgbColorDto(0, 130, 180)),
        (0.50d, new RgbColorDto(40, 170, 90)),
        (0.75d, new RgbColorDto(245, 190, 60)),
        (1.00d, new RgbColorDto(210, 55, 45))
    ];

    public RgbColorDto GetColor(double normalizedValue)
    {
        if (!double.IsFinite(normalizedValue))
        {
            return new RgbColorDto(120, 120, 120);
        }

        var value = Math.Clamp(normalizedValue, 0d, 1d);
        for (var index = 0; index < Stops.Length - 1; index++)
        {
            var left = Stops[index];
            var right = Stops[index + 1];
            if (value < left.Stop || value > right.Stop)
            {
                continue;
            }

            var localValue = (value - left.Stop) / (right.Stop - left.Stop);
            return Interpolate(left.Color, right.Color, localValue);
        }

        return Stops[^1].Color;
    }

    private static RgbColorDto Interpolate(RgbColorDto left, RgbColorDto right, double value) => new(
        (byte)Math.Round(left.R + (right.R - left.R) * value),
        (byte)Math.Round(left.G + (right.G - left.G) * value),
        (byte)Math.Round(left.B + (right.B - left.B) * value));
}
