using Application.DTOs.Charts;
using Application.Configuration;
using Application.Services.Trend;
using Domain.Common;
using Domain.Tags;

namespace Application.Services;

public sealed class ChartDataService
{
    private readonly TagService _tagService;
    private readonly IReadOnlyDictionary<string, TagDefinition> _definitions;
    private readonly ITagRuntimeConfigurationStore? _configurationStore;
    private readonly TrendDiagnosisService _diagnosisService;

    public ChartDataService(TagService tagService)
        : this(
            tagService,
            new Dictionary<string, TagDefinition>(StringComparer.Ordinal),
            null,
            null)
    {
    }

    public ChartDataService(
        TagService tagService,
        IReadOnlyDictionary<string, TagDefinition> definitions,
        ITagRuntimeConfigurationStore? configurationStore,
        TrendDiagnosisOptions? diagnosisOptions = null)
    {
        _tagService = tagService ?? throw new ArgumentNullException(nameof(tagService));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _configurationStore = configurationStore;
        _diagnosisService = new TrendDiagnosisService(diagnosisOptions);
    }

    public TrendSeriesDto GetTrendSeries(string tagId, int pointCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
        var snapshot = _tagService.GetSnapshot();
        return BuildTrendSeries(snapshot, tagId, pointCount);
    }

    public TrendSeriesDto BuildTrendSeries(
        TagSnapshot snapshot,
        string tagId,
        int pointCount,
        IReadOnlyList<TagRuntimeState>? currentValues = null,
        DateTime? maximumTimestampUtc = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
        var points = pointCount > 0 && snapshot.RecentBuffers.TryGetValue(tagId, out var buffer)
            ? buffer
                .Where(point => !maximumTimestampUtc.HasValue || point.Timestamp <= maximumTimestampUtc.Value)
                .TakeLast(pointCount)
                .Select(point => new TrendPointDto(
                    point.Timestamp,
                    point.Value,
                    point.Quality))
                .ToArray()
            : [];
        var sourceState = (currentValues ?? snapshot.CurrentValues)
            .FirstOrDefault(state => state.TagId == tagId);

        return new TrendSeriesDto(
            tagId,
            points,
            Math.Max(pointCount, 0),
            sourceState?.SourceFrameId ?? Guid.Empty,
            sourceState?.SequenceNo ?? 0,
            sourceState?.Timestamp.UtcDateTime);
    }

    public TrendSnapshotDto BuildTrendSnapshot(
        TagSnapshot snapshot,
        string tagId,
        TimeSpan window,
        int expectedPointCount,
        DateTime capturedAt,
        IReadOnlyList<TagRuntimeState>? currentValues = null,
        DateTime? maximumTimestampUtc = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Trend window must be greater than zero.");
        }

        if (expectedPointCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedPointCount),
                "Expected point count must not be negative.");
        }

        UtcDateTime.Require(capturedAt, nameof(capturedAt));
        var effectiveEnd = maximumTimestampUtc.HasValue && maximumTimestampUtc.Value < capturedAt
            ? maximumTimestampUtc.Value
            : capturedAt;
        var windowStart = capturedAt - window;
        var rawPoints = snapshot.RecentBuffers.TryGetValue(tagId, out var buffer)
            ? buffer
                .Where(point => point.Timestamp >= windowStart && point.Timestamp <= effectiveEnd)
                .OrderBy(point => point.Timestamp)
                .Select(point => new TrendPointDto(
                    point.Timestamp,
                    point.Value,
                    point.Quality))
                .ToArray()
            : [];
        var sourceState = (currentValues ?? snapshot.CurrentValues)
            .FirstOrDefault(state => state.TagId == tagId);
        var definition = _definitions.GetValueOrDefault(tagId);
        var configuration = GetConfiguration(tagId);
        var metadata = new TrendTagMetadataDto(
            tagId,
            definition?.DisplayName ?? sourceState?.DisplayName ?? tagId,
            definition?.Unit ?? sourceState?.Unit ?? "",
            definition?.MinValue,
            definition?.MaxValue,
            definition?.DataType ?? sourceState?.DataType ?? TagDataType.Number,
            definition is not null);
        var diagnosisResult = _diagnosisService.Analyze(
            rawPoints,
            metadata.EngineeringMinimum,
            metadata.EngineeringMaximum);
        var series = new TrendSeriesDto(
            tagId,
            diagnosisResult.Points,
            expectedPointCount,
            sourceState?.SourceFrameId ?? Guid.Empty,
            sourceState?.SequenceNo ?? 0,
            sourceState?.Timestamp.UtcDateTime);

        return new TrendSnapshotDto(
            metadata,
            window,
            series,
            BuildThresholds(configuration),
            TrendStatisticsCalculator.Calculate(diagnosisResult.Points),
            diagnosisResult.Diagnosis,
            sourceState?.NumericValue,
            sourceState?.Quality,
            sourceState?.AlarmState,
            sourceState?.Timestamp.UtcDateTime,
            configuration?.Revision ?? 0,
            capturedAt);
    }

    private TagRuntimeConfiguration? GetConfiguration(string tagId)
    {
        if (_configurationStore?.Snapshot.TryGetValue(tagId, out var configuration) == true)
        {
            return configuration;
        }

        return null;
    }

    private static IReadOnlyList<TrendThresholdDto> BuildThresholds(
        TagRuntimeConfiguration? configuration)
    {
        if (configuration is null || !configuration.AlarmEnabled)
        {
            return [];
        }

        var thresholds = new List<TrendThresholdDto>(4);
        AddThreshold(thresholds, "Alarm Low", configuration.AlarmLow, TrendThresholdType.AlarmLow);
        AddThreshold(thresholds, "Warning Low", configuration.WarningLow, TrendThresholdType.WarningLow);
        AddThreshold(thresholds, "Warning High", configuration.WarningHigh, TrendThresholdType.WarningHigh);
        AddThreshold(thresholds, "Alarm High", configuration.AlarmHigh, TrendThresholdType.AlarmHigh);
        return thresholds;
    }

    private static void AddThreshold(
        ICollection<TrendThresholdDto> thresholds,
        string name,
        double? value,
        TrendThresholdType type)
    {
        if (value.HasValue)
        {
            thresholds.Add(new TrendThresholdDto(name, value.Value, type));
        }
    }
}
