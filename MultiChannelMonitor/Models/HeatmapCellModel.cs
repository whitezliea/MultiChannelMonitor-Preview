using System.Windows.Media;
using Application.DTOs.MeasurementMap;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Presentation.Wpf.Models;

public sealed partial class HeatmapCellModel : ObservableObject
{
    public required int Row { get; init; }
    public required int Column { get; init; }
    public required double Value { get; init; }
    public required double NormalizedValue { get; init; }
    public required Brush Background { get; init; }
    public required bool IsValid { get; init; }
    public required bool IsAbnormal { get; init; }
    public required MatrixAbnormalType? AbnormalType { get; init; }
    public required MatrixSeverity? Severity { get; init; }
    public required string DisplayValue { get; init; }
    public required string ValueWithUnit { get; init; }
    public required string TooltipText { get; init; }

    public string CoordinateText => $"R{Row:00} / C{Column:00}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValueVisible))]
    private bool showValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValueVisible))]
    private bool isSelected;

    public bool IsValueVisible => ShowValue || IsSelected || !IsValid;
}
