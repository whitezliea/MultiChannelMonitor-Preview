using System.Globalization;
using Application.DTOs.MeasurementMap;

namespace Application.Services.MeasurementMap;

public sealed class MatrixHeatmapMapper
{
    private readonly MatrixScaleService _scaleService;
    private readonly IndustrialHeatColorMapService _colorMapService;

    public MatrixHeatmapMapper(MatrixScaleService scaleService, IndustrialHeatColorMapService colorMapService)
    {
        _scaleService = scaleService;
        _colorMapService = colorMapService;
    }

    public IReadOnlyList<HeatmapCellDto> Map(
        MatrixFrameDto frame,
        ScaleRangeDto scaleRange,
        IReadOnlyList<AbnormalMatrixPointDto> abnormalPoints,
        string unit)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(scaleRange);
        ArgumentNullException.ThrowIfNull(abnormalPoints);

        var abnormalByCoordinate = abnormalPoints.ToDictionary(point => (point.Row, point.Column));
        var cells = new List<HeatmapCellDto>(frame.Rows * frame.Columns);

        for (var row = 0; row < frame.Rows; row++)
        {
            for (var column = 0; column < frame.Columns; column++)
            {
                var value = frame.Values[row, column];
                var isValid = double.IsFinite(value);
                var normalizedValue = _scaleService.Normalize(value, scaleRange);
                var color = _colorMapService.GetColor(normalizedValue);
                abnormalByCoordinate.TryGetValue((row, column), out var abnormalPoint);
                var displayText = isValid ? value.ToString("0.###", CultureInfo.InvariantCulture) : "NA";
                var tooltipText = $"Row {row}, Col {column}\nValue: {displayText} {unit}";
                if (abnormalPoint is not null)
                {
                    tooltipText += $"\nAbnormal: {abnormalPoint.Type}";
                }

                cells.Add(new HeatmapCellDto(
                    row,
                    column,
                    value,
                    normalizedValue,
                    color,
                    isValid,
                    abnormalPoint is not null,
                    abnormalPoint?.Type,
                    abnormalPoint?.Severity,
                    displayText,
                    tooltipText));
            }
        }

        return cells;
    }
}
