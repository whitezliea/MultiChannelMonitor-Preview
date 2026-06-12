using Application.Abstractions.Events;
using Application.Events;
using Domain.Logs;

namespace Application.Services;

public sealed class DataSourceHealthOperationLogConsumer :
    IApplicationEventHandler<DataSourceTimedOutEvent>,
    IApplicationEventHandler<DataSourceRecoveredEvent>
{
    private readonly OperationLogService _operationLogService;

    public DataSourceHealthOperationLogConsumer(OperationLogService operationLogService)
    {
        _operationLogService = operationLogService;
    }

    public ValueTask HandleAsync(DataSourceTimedOutEvent applicationEvent, CancellationToken cancellationToken) =>
        _operationLogService.WriteAsync(
            OperationLogLevel.Error,
            "Acquisition",
            "DataSource.TimedOut",
            nameof(DataSourceHealthOperationLogConsumer),
            "Data source stopped producing frames.",
            $"FrameId={applicationEvent.LastFrameId}; SequenceNo={applicationEvent.LastSequenceNo}; LastFrameUtc={applicationEvent.LastFrameTimeUtc:O}; TimedOutAtUtc={applicationEvent.TimedOutAtUtc:O}",
            cancellationToken: cancellationToken);

    public ValueTask HandleAsync(DataSourceRecoveredEvent applicationEvent, CancellationToken cancellationToken) =>
        _operationLogService.WriteAsync(
            OperationLogLevel.Info,
            "Acquisition",
            "DataSource.Recovered",
            nameof(DataSourceHealthOperationLogConsumer),
            "Data source resumed producing frames.",
            $"FrameId={applicationEvent.FrameId}; SequenceNo={applicationEvent.SequenceNo}; RecoveredAtUtc={applicationEvent.RecoveredAtUtc:O}",
            cancellationToken: cancellationToken);
}
