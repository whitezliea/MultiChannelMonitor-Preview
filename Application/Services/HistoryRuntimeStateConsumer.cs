using Application.Abstractions.Events;
using Application.Events;
using Application.Configuration;
using AppLogging;
using Domain.Common;
using Domain.Tags;

namespace Application.Services;

public sealed class HistoryRuntimeStateConsumer : IApplicationEventHandler<TagRuntimeStatesProducedEvent>
{
    private readonly HistoryService _historyService;
    private readonly ITagRuntimeConfigurationStore _configurationStore;
    private readonly Dictionary<string, SamplingState> _lastPersisted = [];

    public HistoryRuntimeStateConsumer(
        HistoryService historyService,
        IEnumerable<TagDefinition> definitions)
        : this(
            historyService,
            new TagRuntimeConfigurationStore(
                definitions.Select(TagRuntimeConfiguration.FromDefinition)))
    {
    }

    public HistoryRuntimeStateConsumer(
        HistoryService historyService,
        ITagRuntimeConfigurationStore configurationStore)
    {
        _historyService = historyService;
        _configurationStore = configurationStore;
    }

    public async ValueTask HandleAsync(
        TagRuntimeStatesProducedEvent applicationEvent,
        CancellationToken cancellationToken)
    {
        var configurations = _configurationStore.Snapshot;
        foreach (var state in applicationEvent.States)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UtcDateTime.Require(state.Timestamp, $"{nameof(applicationEvent.States)}.{nameof(state.Timestamp)}");
            if (!configurations.TryGetValue(state.TagId, out var configuration)
                || !ShouldPersist(state, configuration))
            {
                continue;
            }

            var sample = new TagValue(
                state.TagId,
                state.NumericValue!.Value,
                state.Timestamp.UtcDateTime,
                state.Quality,
                state.AlarmState,
                state.SourceFrameId.ToString("D"),
                state.SequenceNo);
            await _historyService.EnqueueAsync(sample, cancellationToken).ConfigureAwait(false);
            _lastPersisted[state.TagId] = new SamplingState(
                state.Timestamp.UtcDateTime,
                state.Quality,
                state.AlarmState,
                configuration.Revision);
        }
    }

    private bool ShouldPersist(
        TagRuntimeState state,
        TagRuntimeConfiguration configuration)
    {
        if (!state.NumericValue.HasValue
            || !configuration.IsHistorized)
        {
            return false;
        }

        if (!_lastPersisted.TryGetValue(state.TagId, out var last)
            || last.Revision != configuration.Revision)
        {
            return true;
        }

        if (state.Timestamp.UtcDateTime < last.TimestampUtc)
        {
            AppLogger.Error(
                "History sampling timestamp moved backwards; baseline reset | TagId: {0} | PreviousUtc: {1:O} | CurrentUtc: {2:O}",
                state.TagId,
                last.TimestampUtc,
                state.Timestamp.UtcDateTime);
            _lastPersisted.Remove(state.TagId);
            return true;
        }

        if (state.Quality != last.Quality || state.AlarmState != last.AlarmState)
        {
            return true;
        }

        return state.Timestamp.UtcDateTime - last.TimestampUtc
            >= TimeSpan.FromMilliseconds(configuration.HistoryIntervalMs);
    }

    private sealed record SamplingState(
        DateTime TimestampUtc,
        TagQuality Quality,
        TagAlarmState AlarmState,
        long Revision);
}
