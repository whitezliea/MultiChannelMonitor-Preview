using System.Windows.Media;
using Application.DTOs.MeasurementMap;

namespace Presentation.Wpf.Models;

public sealed class MatrixPreviewCellModel
{
    public required int Row { get; init; }
    public required int Column { get; init; }
    public required Brush Background { get; init; }
    public required bool IsValid { get; init; }
    public required bool IsAbnormal { get; init; }
    public required MatrixSeverity? Severity { get; init; }
}
