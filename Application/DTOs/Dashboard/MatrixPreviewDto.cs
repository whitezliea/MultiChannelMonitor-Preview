using Application.DTOs.MeasurementMap;

namespace Application.DTOs.Dashboard;

public sealed record MatrixPreviewDto(
    DateTime Timestamp,
    int Rows,
    int Columns,
    string MatrixType,
    string Unit,
    MatrixQualityState QualityState,
    double Maximum,
    double Average,
    double Uniformity,
    int AbnormalCount,
    MatrixPreviewPointDto? MainAbnormalPoint,
    IReadOnlyList<MatrixPreviewCellDto> Cells,
    Guid SourceFrameId = default,
    long SequenceNo = 0);

public sealed record MatrixPreviewCellDto(
    int Row,
    int Column,
    double NormalizedValue,
    RgbColorDto Color,
    bool IsValid,
    bool IsAbnormal,
    MatrixSeverity? Severity);

public sealed record MatrixPreviewPointDto(
    int Row,
    int Column,
    double Value,
    MatrixAbnormalType Type,
    MatrixSeverity Severity);
