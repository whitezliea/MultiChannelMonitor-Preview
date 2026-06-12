using Application.DTOs.MeasurementMap;

namespace Presentation.Wpf.Models;

public sealed class AbnormalMatrixPointModel
{
    public required int Row { get; init; }
    public required int Column { get; init; }
    public required double Value { get; init; }
    public required string ValueText { get; init; }
    public required MatrixAbnormalType Type { get; init; }
    public required MatrixSeverity Severity { get; init; }
    public required string Message { get; init; }

    public string RowText => $"{Row:00}";
    public string ColumnText => $"{Column:00}";
    public string CoordinateText => $"R{Row:00} / C{Column:00}";
}
