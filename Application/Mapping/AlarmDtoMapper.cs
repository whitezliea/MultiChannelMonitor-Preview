using Application.DTOs.Alarms;
using Domain.Alarms;

namespace Application.Mapping;

public static class AlarmDtoMapper
{
    public static ActiveAlarmDto ToDto(AlarmEvent alarm) =>
        new(alarm.AlarmId, alarm.TagId, alarm.Level, alarm.State, alarm.Message, alarm.TriggerTime);
}
