namespace Application.DTOs.MeasurementMap;

public sealed record MatrixAnalysisSnapshotDto(
    DateTime Timestamp,
    MatrixFrameDto Frame,
    MatrixStatisticsDto Statistics,
    IReadOnlyList<AbnormalMatrixPointDto> AbnormalPoints,
    MatrixQualityState QualityState,
    Guid SourceFrameId = default,
    long SequenceNo = 0);
