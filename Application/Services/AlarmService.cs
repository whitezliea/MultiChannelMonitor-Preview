using AppLogging;
using Application.DTOs.Alarms;
using Application.Configuration;
using Domain.Common;
using Domain.Alarms;
using Domain.Tags;

namespace Application.Services;

public sealed class AlarmService
{
    private static readonly TimeSpan ValueUpdateInterval = TimeSpan.FromSeconds(5);
    private const double MinimumAbsoluteValueChange = 1d;
    private const double MinimumRelativeValueChange = 0.05d;
    private readonly Dictionary<string, Guid> _currentAlarmIds = [];
    private readonly Dictionary<Guid, AlarmEvent> _alarmEvents = [];
    private readonly Dictionary<string, long> _evaluatedConfigurationRevisions = [];
    private readonly IReadOnlyDictionary<string, TagDefinition> _definitions;
    private readonly ITagRuntimeConfigurationStore _configurationStore;
    private readonly object _syncRoot = new();

    public AlarmService(IEnumerable<TagDefinition> definitions)
        : this(
            definitions,
            new TagRuntimeConfigurationStore(
                definitions.Select(TagRuntimeConfiguration.FromDefinition)))
    {
    }

    public AlarmService(
        IEnumerable<TagDefinition> definitions,
        ITagRuntimeConfigurationStore configurationStore)
    {
        _definitions = definitions.ToDictionary(definition => definition.TagId, StringComparer.Ordinal);
        _configurationStore = configurationStore;
    }

    public AlarmService()
        : this([])
    {
    }

    public IReadOnlyList<AlarmEvent> GetActiveAlarms() => GetCurrentAlarms();

    public IReadOnlyList<AlarmEvent> GetCurrentAlarms()
    {
        lock (_syncRoot)
        {
            return _currentAlarmIds.Values
                .Select(alarmId => _alarmEvents.TryGetValue(alarmId, out var alarm) ? alarm : null)
                .OfType<AlarmEvent>()
                .Where(alarm => alarm.State is AlarmState.Active or AlarmState.Acknowledged)
                .OrderByDescending(alarm => alarm.TriggerTime)
                .ToArray();
        }
    }

    public IReadOnlyList<AlarmEvent> GetRecentAlarmEvents(int count = 100)
    {
        lock (_syncRoot)
        {
            var alarms = _alarmEvents.Values.OrderByDescending(alarm => alarm.TriggerTime);
            return count <= 0
                ? []
                : alarms.Take(count).ToArray();
        }
    }

    public IReadOnlyList<AlarmEvent> GetAlarmEvents()
    {
        lock (_syncRoot)
        {
            return _alarmEvents.Values.OrderByDescending(alarm => alarm.TriggerTime).ToArray();
        }
    }

    public AlarmCenterSnapshotDto GetSnapshot(int recentCount = 100)
    {
        lock (_syncRoot)
        {
            var allEvents = _alarmEvents.Values
                .OrderByDescending(alarm => alarm.TriggerTime)
                .ToArray();
            var currentAlarms = _currentAlarmIds.Values
                .Select(alarmId => _alarmEvents.TryGetValue(alarmId, out var alarm) ? alarm : null)
                .OfType<AlarmEvent>()
                .Where(alarm => alarm.State is AlarmState.Active or AlarmState.Acknowledged)
                .OrderByDescending(alarm => alarm.TriggerTime)
                .ToArray();
            var recentEvents = recentCount <= 0
                ? []
                : allEvents.Take(recentCount).ToArray();

            return new AlarmCenterSnapshotDto(currentAlarms, recentEvents, allEvents);
        }
    }

    public void RestoreEvents(IEnumerable<AlarmEvent> alarms)
    {
        ArgumentNullException.ThrowIfNull(alarms);

        lock (_syncRoot)
        {
            foreach (var alarm in alarms.OrderByDescending(item => item.TriggerTime))
            {
                UtcDateTime.Require(alarm.TriggerTime, $"{nameof(alarms)}.{nameof(alarm.TriggerTime)}");
                UtcDateTime.Require(alarm.AcknowledgeTime, $"{nameof(alarms)}.{nameof(alarm.AcknowledgeTime)}");
                UtcDateTime.Require(alarm.RecoverTime, $"{nameof(alarms)}.{nameof(alarm.RecoverTime)}");
                _alarmEvents[alarm.AlarmId] = alarm;
                if (alarm.State is AlarmState.Active or AlarmState.Acknowledged)
                {
                    _currentAlarmIds.TryAdd(alarm.TagId, alarm.AlarmId);
                }
            }
        }
    }

    public IReadOnlyList<TagRuntimeState> Evaluate(IReadOnlyList<CleanedTagValue> values, DateTimeOffset lastUpdateTime)
        => EvaluateWithChanges(values, lastUpdateTime).States;

    public AlarmEvaluationResult EvaluateWithChanges(
        IReadOnlyList<CleanedTagValue> values,
        DateTimeOffset lastUpdateTime)
    {
        UtcDateTime.Require(lastUpdateTime, nameof(lastUpdateTime));
        foreach (var value in values)
        {
            UtcDateTime.Require(value.Timestamp, $"{nameof(values)}.{nameof(value.Timestamp)}");
        }

        var configurations = _configurationStore.Snapshot;
        var states = values
            .Select(value =>
            {
                _definitions.TryGetValue(value.TagId, out var definition);
                var numericValue = value.NumericValue
                    ?? (value.BoolValue.HasValue ? value.BoolValue.Value ? 1d : 0d : null);
                configurations.TryGetValue(value.TagId, out var configuration);
                var alarmState = EvaluateAlarmState(numericValue, value.Quality, configuration);

                return new TagRuntimeState(
                    value.TagId,
                    definition?.DisplayName ?? value.TagId,
                    definition?.Category ?? TagCategory.Runtime,
                    value.NumericValue,
                    value.TextValue,
                    value.BoolValue,
                    definition?.Unit ?? value.Unit,
                    definition?.DataType ?? value.DataType,
                    value.Quality,
                    alarmState,
                    value.Timestamp,
                    value.SourceFrameId,
                    value.SequenceNo,
                    lastUpdateTime);
            })
            .ToArray();
        AppLogger.Info("AlarmService | Evaluate");
        lock (_syncRoot)
        {
            var lifecycleChanges = UpdateActiveAlarms(states, configurations);
            return new AlarmEvaluationResult(states, lifecycleChanges);
        }
    }

    private IReadOnlyList<AlarmLifecycleChange> UpdateActiveAlarms(
        IReadOnlyList<TagRuntimeState> values,
        IReadOnlyDictionary<string, TagRuntimeConfiguration> configurations)
    {
        var lifecycleChanges = new List<AlarmLifecycleChange>();
        foreach (var value in values)
        {
            configurations.TryGetValue(value.TagId, out var configuration);
            var configurationChanged = configuration is not null
                && _evaluatedConfigurationRevisions.TryGetValue(value.TagId, out var previousRevision)
                && previousRevision != configuration.Revision;
            if (configuration is not null)
            {
                _evaluatedConfigurationRevisions[value.TagId] = configuration.Revision;
            }
            if (value.AlarmState is TagAlarmState.Normal)
            {
                var reason = configurationChanged || configuration is { AlarmEnabled: false }
                    ? "ConfigurationChanged"
                    : "ValueReturnedToNormal";
                var recoveredAlarm = Recover(value, reason);
                if (recoveredAlarm is not null)
                {
                    lifecycleChanges.Add(new AlarmLifecycleChange(
                        AlarmLifecycleChangeType.Recovered,
                        recoveredAlarm));
                }
                continue;
            }

            var level = GetAlarmLevel(value.AlarmState);
            var triggerValue = GetTriggerValue(value);
            var message = $"{value.TagId} {value.AlarmState}: {FormatTriggerValue(value)}";

            if (_currentAlarmIds.TryGetValue(value.TagId, out var alarmId)
                && _alarmEvents.TryGetValue(alarmId, out var existingAlarm))
            {
                var shouldPublishUpdate = existingAlarm.Level != level
                    || existingAlarm.AlarmType != value.AlarmState
                    || ShouldPublishValueUpdate(existingAlarm, triggerValue, value.Timestamp.UtcDateTime);
                if (!shouldPublishUpdate)
                {
                    continue;
                }

                var updatedAlarm = existingAlarm with
                {
                    Level = level,
                    AlarmType = value.AlarmState,
                    TriggerValue = triggerValue,
                    Message = message,
                    LastUpdatedTime = value.Timestamp.UtcDateTime
                };
                _alarmEvents[alarmId] = updatedAlarm;
                lifecycleChanges.Add(new AlarmLifecycleChange(
                    AlarmLifecycleChangeType.Updated,
                    updatedAlarm));
                continue;
            }

            var alarm = new AlarmEvent(
                Guid.NewGuid(),
                value.TagId,
                level,
                AlarmState.Active,
                triggerValue,
                value.Timestamp.UtcDateTime,
                message,
                AlarmType: value.AlarmState,
                LastUpdatedTime: value.Timestamp.UtcDateTime);
            _alarmEvents[alarm.AlarmId] = alarm;
            _currentAlarmIds[value.TagId] = alarm.AlarmId;
            lifecycleChanges.Add(new AlarmLifecycleChange(
                AlarmLifecycleChangeType.Raised,
                alarm));
        }

        return lifecycleChanges;
    }

    public bool Acknowledge(Guid alarmId, DateTime acknowledgedAt)
        => TryAcknowledge(alarmId, acknowledgedAt, out _);

    public bool TryAcknowledge(Guid alarmId, DateTime acknowledgedAt, out AlarmEvent? acknowledgedAlarm)
    {
        UtcDateTime.Require(acknowledgedAt, nameof(acknowledgedAt));
        lock (_syncRoot)
        {
            if (!_alarmEvents.TryGetValue(alarmId, out var alarm)
                || !_currentAlarmIds.ContainsValue(alarmId)
                || alarm.State is not AlarmState.Active)
            {
                acknowledgedAlarm = null;
                return false;
            }

            acknowledgedAlarm = alarm with
            {
                State = AlarmState.Acknowledged,
                AcknowledgeTime = acknowledgedAt,
                LastUpdatedTime = acknowledgedAt
            };
            _alarmEvents[alarmId] = acknowledgedAlarm;
            return true;
        }
    }

    private AlarmEvent? Recover(TagRuntimeState value, string reason)
    {
        if (!_currentAlarmIds.TryGetValue(value.TagId, out var alarmId)
            || !_alarmEvents.TryGetValue(alarmId, out var alarm))
        {
            return null;
        }

        var recoveredAlarm = alarm with
        {
            State = AlarmState.Recovered,
            RecoverTime = alarm.RecoverTime ?? value.Timestamp.UtcDateTime,
            Message = $"{value.TagId} recovered: {reason}",
            LastUpdatedTime = value.Timestamp.UtcDateTime,
            CloseReason = reason
        };
        _alarmEvents[alarmId] = recoveredAlarm;
        _currentAlarmIds.Remove(value.TagId);
        return recoveredAlarm;
    }

    private static AlarmLevel GetAlarmLevel(TagAlarmState alarmState) =>
        alarmState is TagAlarmState.WarningHigh or TagAlarmState.WarningLow
            ? AlarmLevel.Warning
            : alarmState is TagAlarmState.Invalid or TagAlarmState.Offline
                ? AlarmLevel.Quality
                : AlarmLevel.Alarm;

    private static TagAlarmState EvaluateAlarmState(
        double? value,
        TagQuality quality,
        TagRuntimeConfiguration? configuration)
    {
        if (configuration is { AlarmEnabled: false })
        {
            return TagAlarmState.Normal;
        }

        if (quality == TagQuality.Offline)
        {
            return TagAlarmState.Offline;
        }

        if (quality != TagQuality.Good)
        {
            return TagAlarmState.Invalid;
        }

        if (configuration is null || !configuration.AlarmEnabled || !value.HasValue)
        {
            return TagAlarmState.Normal;
        }

        if (configuration.AlarmHigh.HasValue && value.Value >= configuration.AlarmHigh.Value)
        {
            return TagAlarmState.AlarmHigh;
        }

        if (configuration.AlarmLow.HasValue && value.Value <= configuration.AlarmLow.Value)
        {
            return TagAlarmState.AlarmLow;
        }

        if (configuration.WarningHigh.HasValue && value.Value >= configuration.WarningHigh.Value)
        {
            return TagAlarmState.WarningHigh;
        }

        if (configuration.WarningLow.HasValue && value.Value <= configuration.WarningLow.Value)
        {
            return TagAlarmState.WarningLow;
        }

        return TagAlarmState.Normal;
    }

    private static string FormatTriggerValue(TagRuntimeState value)
    {
        if (value.NumericValue.HasValue)
        {
            return value.NumericValue.Value.ToString("0.###");
        }

        if (value.BoolValue.HasValue)
        {
            return value.BoolValue.Value ? "True" : "False";
        }

        return value.TextValue ?? "";
    }

    private static double GetTriggerValue(TagRuntimeState value) =>
        value.NumericValue ?? (value.BoolValue.HasValue ? value.BoolValue.Value ? 1d : 0d : 0d);

    private static bool ShouldPublishValueUpdate(
        AlarmEvent existingAlarm,
        double triggerValue,
        DateTime timestampUtc)
    {
        var lastUpdatedUtc = existingAlarm.LastUpdatedTime ?? existingAlarm.TriggerTime;
        if (timestampUtc - lastUpdatedUtc >= ValueUpdateInterval)
        {
            return true;
        }

        var absoluteChange = Math.Abs(triggerValue - existingAlarm.TriggerValue);
        var relativeBaseline = Math.Max(Math.Abs(existingAlarm.TriggerValue), 1d);
        return absoluteChange >= MinimumAbsoluteValueChange
            && absoluteChange / relativeBaseline >= MinimumRelativeValueChange;
    }
}
