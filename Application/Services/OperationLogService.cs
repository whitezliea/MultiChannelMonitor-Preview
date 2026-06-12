using Application.Abstractions.Persistence;
using Application.Abstractions.Time;
using Application.Queues;
using Domain.Common;
using Domain.Logs;

namespace Application.Services;

public sealed class OperationLogService
{
    private readonly IOperationLogRepository _repository;
    private readonly OperationLogQueue _queue;
    private readonly IClock _clock;

    public OperationLogService(
        IOperationLogRepository repository,
        OperationLogQueue queue,
        IClock clock)
    {
        _repository = repository;
        _queue = queue;
        _clock = clock;
    }

    public ValueTask EnqueueAsync(OperationLog log, CancellationToken cancellationToken)
    {
        UtcDateTime.Require(log.Timestamp, nameof(log.Timestamp));
        return _queue.EnqueueAsync(log, cancellationToken);
    }

    public ValueTask WriteAsync(
        OperationLogLevel level,
        string category,
        string action,
        string source,
        string message,
        string? detail = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return EnqueueAsync(
            new OperationLog(
                _clock.UtcNow,
                level,
                category,
                message,
                action,
                source,
                detail,
                correlationId),
            cancellationToken);
    }

    public Task<IReadOnlyList<OperationLog>> QueryLatestAsync(int count, CancellationToken cancellationToken) =>
        _repository.QueryLatestAsync(count, cancellationToken);

    public Task<IReadOnlyList<OperationLog>> QueryAsync(
        OperationLogQuery query,
        CancellationToken cancellationToken) =>
        _repository.QueryAsync(query, cancellationToken);
}
