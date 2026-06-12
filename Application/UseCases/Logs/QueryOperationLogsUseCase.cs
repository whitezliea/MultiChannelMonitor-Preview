using Application.Services;
using Domain.Logs;

namespace Application.UseCases.Logs;

public sealed class QueryOperationLogsUseCase
{
    private readonly OperationLogService _operationLogService;
    private readonly PersistenceRuntimeCoordinator _persistenceRuntime;

    public QueryOperationLogsUseCase(
        OperationLogService operationLogService,
        PersistenceRuntimeCoordinator persistenceRuntime)
    {
        _operationLogService = operationLogService;
        _persistenceRuntime = persistenceRuntime;
    }

    public async Task<IReadOnlyList<OperationLog>> ExecuteAsync(
        OperationLogQuery query,
        CancellationToken cancellationToken = default)
    {
        await _persistenceRuntime.FlushOperationLogsAsync(cancellationToken).ConfigureAwait(false);
        var result = await _operationLogService.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        await _operationLogService.WriteAsync(
            OperationLogLevel.Info,
            "OperationLog",
            "OperationLog.Queried",
            nameof(QueryOperationLogsUseCase),
            "Operation logs queried.",
            $"StartUtc={query.StartTimeUtc:O}; EndUtc={query.EndTimeUtc:O}; " +
            $"Level={query.Level?.ToString() ?? "All"}; Category={query.Category ?? "All"}; " +
            $"MaxCount={query.MaxCount}; ResultCount={result.Count}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result;
    }
}
