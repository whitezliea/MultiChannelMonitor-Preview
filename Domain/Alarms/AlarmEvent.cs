namespace Domain.Alarms;

using Domain.Tags;

public sealed record AlarmEvent(
    Guid AlarmId,
    string TagId,
    AlarmLevel Level,
    AlarmState State,
    double TriggerValue,
    DateTime TriggerTime,
    string Message,
    DateTime? AcknowledgeTime = null,
    DateTime? RecoverTime = null,
    TagAlarmState AlarmType = TagAlarmState.Invalid,
    DateTime? LastUpdatedTime = null,
    string? CloseReason = null);
