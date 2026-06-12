using Domain.Alarms;

namespace Application.Events;

public sealed record AlarmRaisedEvent(AlarmEvent Alarm);
public sealed record AlarmUpdatedEvent(AlarmEvent Alarm);
public sealed record AlarmRecoveredEvent(AlarmEvent Alarm);
public sealed record AlarmAcknowledgedEvent(AlarmEvent Alarm);
