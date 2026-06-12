using Domain.Tags;

namespace Application.Configuration;

public sealed record TagRuntimeConfiguration(
    string TagId,
    bool AlarmEnabled,
    double? WarningLow,
    double? AlarmLow,
    double? WarningHigh,
    double? AlarmHigh,
    bool IsHistorized,
    int HistoryIntervalMs,
    long Revision = 0)
{
    public static TagRuntimeConfiguration FromDefinition(TagDefinition definition) =>
        new(
            definition.TagId,
            definition.IsEnabled,
            definition.WarningLow,
            definition.AlarmLow,
            definition.WarningHigh,
            definition.AlarmHigh,
            definition.IsHistorized,
            definition.HistoryIntervalMs ?? 1000);
}

public interface ITagRuntimeConfigurationStore
{
    IReadOnlyDictionary<string, TagRuntimeConfiguration> Snapshot { get; }
    TagRuntimeConfiguration Get(string tagId);
    void Replace(IEnumerable<TagRuntimeConfiguration> configurations);
}

public sealed class TagRuntimeConfigurationStore : ITagRuntimeConfigurationStore
{
    private IReadOnlyDictionary<string, TagRuntimeConfiguration> _snapshot;
    private long _revision;

    public TagRuntimeConfigurationStore(IEnumerable<TagRuntimeConfiguration> defaults)
    {
        _snapshot = CreateSnapshot(defaults, incrementRevision: false);
    }

    public IReadOnlyDictionary<string, TagRuntimeConfiguration> Snapshot =>
        Volatile.Read(ref _snapshot);

    public TagRuntimeConfiguration Get(string tagId) =>
        Snapshot.TryGetValue(tagId, out var configuration)
            ? configuration
            : throw new KeyNotFoundException($"Unknown TagId: {tagId}");

    public void Replace(IEnumerable<TagRuntimeConfiguration> configurations) =>
        Volatile.Write(ref _snapshot, CreateSnapshot(configurations, incrementRevision: true));

    private IReadOnlyDictionary<string, TagRuntimeConfiguration> CreateSnapshot(
        IEnumerable<TagRuntimeConfiguration> configurations,
        bool incrementRevision)
    {
        var revision = incrementRevision ? Interlocked.Increment(ref _revision) : _revision;
        return configurations.ToDictionary(
            item => item.TagId,
            item => item with { Revision = revision },
            StringComparer.Ordinal);
    }
}
