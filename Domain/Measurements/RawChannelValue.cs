using Domain.Tags;

namespace Domain.Measurements;

public sealed record RawChannelValue(
    string Code,
    double? Value,
    string Unit,
    TagQuality Quality,
    int ErrorCode);
