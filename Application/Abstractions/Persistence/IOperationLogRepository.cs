using Domain.Logs;

namespace Application.Abstractions.Persistence;

public interface IOperationLogRepository
{
    Task AppendAsync(IReadOnlyCollection<OperationLog> logs, CancellationToken cancellationToken);
    Task<IReadOnlyList<OperationLog>> QueryLatestAsync(int count, CancellationToken cancellationToken);
    Task<IReadOnlyList<OperationLog>> QueryAsync(
        OperationLogQuery query,
        CancellationToken cancellationToken);
}
