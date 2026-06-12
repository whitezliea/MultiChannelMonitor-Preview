using Domain.Tags;

namespace Domain.Measurements;

public sealed record ChannelValue(
    string ChannelId,
    double Value,
    string Unit,
    TagQuality Quality = TagQuality.Good,
    int ErrorCode = 0);
