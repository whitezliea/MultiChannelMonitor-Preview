using Application.Abstractions.Events;
using Application.Events;
using Domain.Alarms;
using Domain.Logs;

namespace Application.Services;

public sealed class AlarmOperationLogConsumer :
    IApplicationEventHandler<AlarmRaisedEvent>,
    IApplicationEventHandler<AlarmUpdatedEvent>,
    IApplicationEventHandler<AlarmRecoveredEvent>,
    IApplicationEventHandler<AlarmAcknowledgedEvent>
{
    private readonly OperationLogService _operationLogService;

    public AlarmOperationLogConsumer(OperationLogService operationLogService)
    {
        _operationLogService = operationLogService;
    }

    public ValueTask HandleAsync(AlarmRaisedEvent applicationEvent, CancellationToken cancellationToken) =>
        WriteAsync(applicationEvent.Alarm, "Alarm.Raised", "Alarm raised.", cancellationToken);

    public ValueTask HandleAsync(AlarmUpdatedEvent applicationEvent, CancellationToken cancellationToken) =>
        WriteAsync(applicationEvent.Alarm, "Alarm.Updated", "Alarm level updated.", cancellationToken);

    public ValueTask HandleAsync(AlarmRecoveredEvent applicationEvent, CancellationToken cancellationToken) =>
        WriteAsync(applicationEvent.Alarm, "Alarm.Recovered", "Alarm recovered.", cancellationToken);

    public ValueTask HandleAsync(AlarmAcknowledgedEvent applicationEvent, CancellationToken cancellationToken) =>
        WriteAsync(applicationEvent.Alarm, "Alarm.Acknowledged", "Alarm acknowledged.", cancellationToken);

    private ValueTask WriteAsync(
        AlarmEvent alarm,
        string action,
        string message,
        CancellationToken cancellationToken) =>
        _operationLogService.WriteAsync(
            alarm.Level == AlarmLevel.Alarm ? OperationLogLevel.Error : OperationLogLevel.Warning,
            "Alarm",
            action,
            nameof(AlarmOperationLogConsumer),
            message,
            $"TagId={alarm.TagId}; Level={alarm.Level}; Type={alarm.AlarmType}; State={alarm.State}; Value={alarm.TriggerValue}; CloseReason={alarm.CloseReason}; Message={alarm.Message}",
            alarm.AlarmId.ToString("D"),
            cancellationToken);
}
