using Domain.Alarms;

namespace Application.DTOs.Alarms;

public sealed record AlarmCenterSnapshotDto(
    IReadOnlyList<AlarmEvent> CurrentAlarms,
    IReadOnlyList<AlarmEvent> RecentEvents,
    IReadOnlyList<AlarmEvent> AllEvents);
