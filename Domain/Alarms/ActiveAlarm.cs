namespace Domain.Alarms;

public sealed record ActiveAlarm(AlarmEvent Event, bool IsAcknowledged);
