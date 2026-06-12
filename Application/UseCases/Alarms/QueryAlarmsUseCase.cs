using Application.Abstractions.Persistence;
using Application.Services;
using Domain.Logs;

namespace Application.UseCases.Alarms;

public sealed class QueryAlarmsUseCase
{
    private readonly IAlarmRepository _repository;
    private readonly OperationLogService? _operationLogService;

    public QueryAlarmsUseCase(
        IAlarmRepository repository,
        OperationLogService? operationLogService = null)
    {
        _repository = repository;
        _operationLogService = operationLogService;
    }

    public Task<IReadOnlyList<Domain.Alarms.AlarmEvent>> QueryRecentAsync(
        int count,
        CancellationToken cancellationToken = default) =>
        _repository.QueryLatestAsync(count, cancellationToken);

    public async Task<AlarmQueryResult> QueryHistoryAsync(
        AlarmQuery query,
        CancellationToken cancellationToken = default)
    {
        query.Validate();
        var result = await _repository.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        if (_operationLogService is not null)
        {
            await _operationLogService.WriteAsync(
                OperationLogLevel.Info,
                "Alarm",
                "Alarm.HistoryQueried",
                nameof(QueryAlarmsUseCase),
                "Alarm history queried.",
                $"StartUtc={query.StartTimeUtc:O}; EndUtc={query.EndTimeUtc:O}; TagId={query.TagId}; Level={query.Level}; State={query.State}; Page={query.Page}; Count={result.Items.Count}; Total={result.TotalCount}",
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        return result;
    }
}
