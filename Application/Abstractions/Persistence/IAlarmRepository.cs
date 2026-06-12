using Domain.Alarms;

namespace Application.Abstractions.Persistence;

public interface IAlarmRepository
{
    Task AppendAsync(IReadOnlyCollection<AlarmEvent> alarms, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlarmEvent>> QueryLatestAsync(int count, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlarmEvent>> QueryOpenAlarmsAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("Open alarm query is not implemented by this repository.");

    Task<AlarmQueryResult> QueryAsync(AlarmQuery query, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Paged alarm query is not implemented by this repository.");
}
