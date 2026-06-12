using System.Windows.Media;
using Application.DTOs.Dashboard;
using Application.DTOs.MeasurementMap;
using Presentation.Wpf.Models;

namespace Presentation.Wpf.Renderers;

public sealed class HeatmapRenderer
{
    private readonly Dictionary<int, SolidColorBrush> _brushCache = [];

    public HeatmapCellModel CreateCellModel(HeatmapCellDto cell, string unit, bool showValue)
    {
        ArgumentNullException.ThrowIfNull(cell);

        return new HeatmapCellModel
        {
            Row = cell.Row,
            Column = cell.Column,
            Value = cell.Value,
            NormalizedValue = cell.NormalizedValue,
            Background = GetBrush(cell.Color),
            IsValid = cell.IsValid,
            IsAbnormal = cell.IsAbnormal,
            AbnormalType = cell.AbnormalType,
            Severity = cell.Severity,
            DisplayValue = cell.DisplayText,
            ValueWithUnit = cell.IsValid ? $"{cell.DisplayText} {unit}" : "NA",
            TooltipText = cell.TooltipText,
            ShowValue = showValue
        };
    }

    public MatrixPreviewCellModel CreatePreviewCellModel(MatrixPreviewCellDto cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        return new MatrixPreviewCellModel
        {
            Row = cell.Row,
            Column = cell.Column,
            Background = GetBrush(cell.Color),
            IsValid = cell.IsValid,
            IsAbnormal = cell.IsAbnormal,
            Severity = cell.Severity
        };
    }

    public string BuildRenderSummary(MatrixFrameDto frame) =>
        $"{frame.Rows}x{frame.Columns} matrix, max {frame.Statistics.MaxValue:0.###}.";

    private Brush GetBrush(RgbColorDto color)
    {
        var red = Quantize(color.R);
        var green = Quantize(color.G);
        var blue = Quantize(color.B);
        var key = red << 16 | green << 8 | blue;

        if (_brushCache.TryGetValue(key, out var brush))
        {
            return brush;
        }

        brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        _brushCache[key] = brush;
        return brush;
    }

    private static byte Quantize(byte value) =>
        (byte)Math.Clamp((int)Math.Round(value / 16d) * 16, byte.MinValue, byte.MaxValue);
}
