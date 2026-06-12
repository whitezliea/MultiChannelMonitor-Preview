using Domain.Tags;

namespace Application.DTOs.Charts;

public enum TrendThresholdType
{
    AlarmLow,
    WarningLow,
    WarningHigh,
    AlarmHigh
}

public enum TrendDiagnosisState
{
    NotEvaluated,
    InsufficientData,
    Stable,
    Spike,
    Drift
}

public sealed record TrendTagMetadataDto(
    string TagId,
    string DisplayName,
    string Unit,
    double? EngineeringMinimum,
    double? EngineeringMaximum,
    TagDataType DataType,
    bool IsKnownTag);

public sealed record TrendThresholdDto(
    string Name,
    double Value,
    TrendThresholdType Type);

public sealed record TrendStatisticsDto(
    double? Last,
    double? Minimum,
    double? Maximum,
    double? Average,
    double? StdDev,
    int TotalCount,
    int ValidCount);

public sealed record TrendDiagnosisDto(
    TrendDiagnosisState State,
    bool HasSpike,
    bool HasDrift,
    string Message,
    int SpikeCount = 0,
    double? SlopePerMinute = null,
    double? NormalizedSlopePercentPerMinute = null);

public sealed record TrendSnapshotDto(
    TrendTagMetadataDto Metadata,
    TimeSpan Window,
    TrendSeriesDto Series,
    IReadOnlyList<TrendThresholdDto> Thresholds,
    TrendStatisticsDto Statistics,
    TrendDiagnosisDto Diagnosis,
    double? CurrentValue,
    TagQuality? CurrentQuality,
    TagAlarmState? CurrentAlarmState,
    DateTime? CurrentTimestamp,
    long ConfigurationRevision,
    DateTime CapturedAt)
{
    public string TagId => Metadata.TagId;
    public int ExpectedPointCount => Series.RequestedPointCount;
    public bool IsWindowComplete => Series.IsWindowComplete;
    public Guid SourceFrameId => Series.SourceFrameId;
    public long SequenceNo => Series.SequenceNo;
    public DateTime? SourceTimestamp => Series.SourceTimestamp;
}
