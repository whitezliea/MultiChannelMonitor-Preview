using Application.Abstractions.Persistence;
using Domain.Logs;

namespace Infrastructure.Persistence;

public sealed class InMemoryOperationLogRepository : IOperationLogRepository
{
    private readonly List<OperationLog> _logs = [];
    private readonly object _syncRoot = new();

    public Task AppendAsync(IReadOnlyCollection<OperationLog> logs, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            _logs.AddRange(logs);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OperationLog>> QueryLatestAsync(int count, CancellationToken cancellationToken)
    {
        return QueryAsync(
            new OperationLogQuery(
                DateTime.UnixEpoch,
                DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
                MaxCount: Math.Max(0, count)),
            cancellationToken);
    }

    public Task<IReadOnlyList<OperationLog>> QueryAsync(
        OperationLogQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            var result = _logs
                .Where(log => log.Timestamp >= query.StartTimeUtc && log.Timestamp <= query.EndTimeUtc)
                .Where(log => !query.Level.HasValue || log.Level == query.Level.Value)
                .Where(log => string.IsNullOrWhiteSpace(query.Category)
                    || string.Equals(log.Category, query.Category.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(log => log.Timestamp)
                .Take(query.MaxCount)
                .ToArray();
            return Task.FromResult<IReadOnlyList<OperationLog>>(result);
        }
    }
}
