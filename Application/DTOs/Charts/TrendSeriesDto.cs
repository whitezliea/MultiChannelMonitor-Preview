namespace Application.DTOs.Charts;

using Domain.Tags;

public sealed record TrendPointDto(
    DateTime Timestamp,
    double Value,
    TagQuality Quality = TagQuality.Good,
    bool IsSpike = false);

public sealed record TrendSeriesDto(
    string TagId,
    IReadOnlyList<TrendPointDto> Points,
    int RequestedPointCount,
    Guid SourceFrameId = default,
    long SequenceNo = 0,
    DateTime? SourceTimestamp = null)
{
    public bool IsWindowComplete => RequestedPointCount > 0 && Points.Count >= RequestedPointCount;
}
