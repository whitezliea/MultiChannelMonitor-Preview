using Domain.Alarms;
using Domain.Tags;

namespace Application.DTOs.Dashboard;

public sealed record DashboardSnapshotDto(
    DateTime Timestamp,
    IReadOnlyList<TagRuntimeState> Tags,
    IReadOnlyList<AlarmEvent> ActiveAlarms,
    int TotalTagCount,
    int BadQualityCount,
    Guid SourceFrameId = default,
    long SequenceNo = 0);
