using Application.Abstractions.Events;
using Application.Events;
using Application.Queues;
using Domain.Alarms;

namespace Application.Services;

public sealed class AlarmEventConsumer :
    IApplicationEventHandler<AlarmRaisedEvent>,
    IApplicationEventHandler<AlarmUpdatedEvent>,
    IApplicationEventHandler<AlarmRecoveredEvent>,
    IApplicationEventHandler<AlarmAcknowledgedEvent>
{
    private readonly AlarmEventQueue _queue;

    public AlarmEventConsumer(AlarmEventQueue queue)
    {
        _queue = queue;
    }

    public ValueTask HandleAsync(AlarmRaisedEvent applicationEvent, CancellationToken cancellationToken) =>
        StoreAsync(applicationEvent.Alarm, cancellationToken);

    public ValueTask HandleAsync(AlarmUpdatedEvent applicationEvent, CancellationToken cancellationToken) =>
        StoreAsync(applicationEvent.Alarm, cancellationToken);

    public ValueTask HandleAsync(AlarmRecoveredEvent applicationEvent, CancellationToken cancellationToken) =>
        StoreAsync(applicationEvent.Alarm, cancellationToken);

    public ValueTask HandleAsync(AlarmAcknowledgedEvent applicationEvent, CancellationToken cancellationToken) =>
        StoreAsync(applicationEvent.Alarm, cancellationToken);

    private ValueTask StoreAsync(AlarmEvent alarm, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _queue.EnqueueAsync(alarm, cancellationToken);
    }
}
