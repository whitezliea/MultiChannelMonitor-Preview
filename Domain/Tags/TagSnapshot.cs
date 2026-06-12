namespace Domain.Tags;

public sealed record TrendPoint(
    DateTime Timestamp,
    double Value,
    TagQuality Quality = TagQuality.Good);

public sealed record TagSnapshot(
    IReadOnlyList<TagRuntimeState> CurrentValues,
    IReadOnlyDictionary<string, IReadOnlyList<TrendPoint>> RecentBuffers);
