using Domain.Alarms;

namespace Application.DTOs.Alarms;

public sealed record ActiveAlarmDto(Guid AlarmId, string TagId, AlarmLevel Level, AlarmState State, string Message, DateTime TriggerTime);
