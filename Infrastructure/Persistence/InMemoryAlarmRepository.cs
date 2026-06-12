using Application.Abstractions.Persistence;
using Domain.Alarms;

namespace Infrastructure.Persistence;

public sealed class InMemoryAlarmRepository : IAlarmRepository
{
    private readonly Dictionary<Guid, AlarmEvent> _alarms = [];
    private readonly object _syncRoot = new();

    public Task AppendAsync(IReadOnlyCollection<AlarmEvent> alarms, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            foreach (var alarm in alarms) _alarms[alarm.AlarmId] = alarm;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AlarmEvent>> QueryLatestAsync(int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            return Task.FromResult<IReadOnlyList<AlarmEvent>>(
                _alarms.Values.OrderByDescending(alarm => alarm.TriggerTime).Take(Math.Max(0, count)).ToArray());
        }
    }

    public Task<IReadOnlyList<AlarmEvent>> QueryOpenAlarmsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            return Task.FromResult<IReadOnlyList<AlarmEvent>>(
                _alarms.Values
                    .Where(alarm => alarm.State is AlarmState.Active or AlarmState.Acknowledged)
                    .OrderByDescending(alarm => alarm.TriggerTime)
                    .ToArray());
        }
    }

    public Task<AlarmQueryResult> QueryAsync(AlarmQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        query.Validate();
        lock (_syncRoot)
        {
            var filtered = _alarms.Values
                .Where(alarm => alarm.TriggerTime >= query.StartTimeUtc && alarm.TriggerTime <= query.EndTimeUtc)
                .Where(alarm => string.IsNullOrWhiteSpace(query.TagId)
                    || string.Equals(alarm.TagId, query.TagId.Trim(), StringComparison.Ordinal))
                .Where(alarm => !query.Level.HasValue || alarm.Level == query.Level.Value)
                .Where(alarm => !query.State.HasValue || alarm.State == query.State.Value);
            var ordered = query.SortDirection == AlarmSortDirection.Ascending
                ? filtered.OrderBy(alarm => alarm.TriggerTime).ThenBy(alarm => alarm.AlarmId)
                : filtered.OrderByDescending(alarm => alarm.TriggerTime).ThenByDescending(alarm => alarm.AlarmId);
            var total = ordered.LongCount();
            var items = ordered.Skip(checked((query.Page - 1) * query.PageSize)).Take(query.PageSize).ToArray();
            return Task.FromResult(new AlarmQueryResult(items, total, query.Page, query.PageSize));
        }
    }
}
