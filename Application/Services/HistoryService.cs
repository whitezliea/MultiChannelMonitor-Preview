using Application.Abstractions.Persistence;
using Application.Queues;
using Domain.Tags;

namespace Application.Services;

public sealed class HistoryService
{
    private readonly IHistoryRepository _repository;
    private readonly HistorySampleQueue _queue;
    private readonly OperationLogService? _operationLogService;

    public HistoryService(
        IHistoryRepository repository,
        HistorySampleQueue queue,
        OperationLogService? operationLogService = null)
    {
        _repository = repository;
        _queue = queue;
        _operationLogService = operationLogService;
    }

    public ValueTask EnqueueAsync(TagValue sample, CancellationToken cancellationToken) =>
        _queue.EnqueueAsync(sample, cancellationToken);

    public async Task<HistoryQueryResult<TagValue>> QueryAsync(
        HistoryQuery query,
        CancellationToken cancellationToken)
    {
        query.Validate();
        var result = await _repository
            .QueryAsync(query, cancellationToken)
            .ConfigureAwait(false);
        if (_operationLogService is not null)
        {
            await _operationLogService.WriteAsync(
                Domain.Logs.OperationLogLevel.Info,
                "History",
                "History.Queried",
                nameof(HistoryService),
                "History samples queried.",
                $"TagId={query.TagId}; StartUtc={query.StartTimeUtc:O}; EndUtc={query.EndTimeUtc:O}; Page={query.Page}; PageSize={query.PageSize}; Count={result.Items.Count}; Total={result.TotalCount}",
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
