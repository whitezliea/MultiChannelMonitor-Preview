using Application.Abstractions.Persistence;
using Application.Configuration;
using Application.Services;
using Domain.Tags;

namespace Application.UseCases.History;

public sealed class QueryHistorySamplesUseCase
{
    private readonly HistoryService _historyService;
    private readonly ITagRuntimeConfigurationStore? _configurationStore;

    public QueryHistorySamplesUseCase(
        HistoryService historyService,
        ITagRuntimeConfigurationStore? configurationStore = null)
    {
        _historyService = historyService;
        _configurationStore = configurationStore;
    }

    public Task<HistoryQueryResult<TagValue>> ExecuteAsync(
        HistoryQuery query,
        CancellationToken cancellationToken)
    {
        query.Validate();
        if (_configurationStore is not null
            && (!_configurationStore.Snapshot.TryGetValue(query.TagId, out var configuration)
                || !configuration.IsHistorized))
        {
            throw new ArgumentException($"Tag is not configured for history: {query.TagId}", nameof(query));
        }

        return _historyService.QueryAsync(query, cancellationToken);
    }
}
