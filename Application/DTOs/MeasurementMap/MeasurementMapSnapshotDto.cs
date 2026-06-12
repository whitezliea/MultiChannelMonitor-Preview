namespace Application.DTOs.MeasurementMap;

public enum MatrixScaleMode
{
    AutoCurrentFrame,
    FixedEngineeringRange
}

public enum MatrixPalette
{
    IndustrialHeat
}

public enum MatrixQualityState
{
    Good,
    Attention,
    Warning,
    Alarm
}

public readonly record struct RgbColorDto(byte R, byte G, byte B);

public sealed record ScaleRangeDto(double MinValue, double MaxValue)
{
    public double Range => MaxValue - MinValue;
}

public sealed record MatrixDisplayOptionsDto(
    MatrixScaleMode ScaleMode = MatrixScaleMode.AutoCurrentFrame,
    double? FixedMin = null,
    double? FixedMax = null,
    MatrixPalette Palette = MatrixPalette.IndustrialHeat,
    string MatrixType = "Light Intensity",
    string Unit = "lux");

public sealed record HeatmapCellDto(
    int Row,
    int Column,
    double Value,
    double NormalizedValue,
    RgbColorDto Color,
    bool IsValid,
    bool IsAbnormal,
    MatrixAbnormalType? AbnormalType,
    MatrixSeverity? Severity,
    string DisplayText,
    string TooltipText);

public sealed record MeasurementMapSnapshotDto(
    DateTime Timestamp,
    string MatrixType,
    string Unit,
    MatrixFrameDto Frame,
    ScaleRangeDto ScaleRange,
    MatrixStatisticsDto Statistics,
    IReadOnlyList<HeatmapCellDto> Cells,
    IReadOnlyList<AbnormalMatrixPointDto> AbnormalPoints,
    MatrixQualityState QualityState,
    Guid SourceFrameId = default,
    long SequenceNo = 0);
