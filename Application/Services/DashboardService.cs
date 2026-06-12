using Application.DTOs.Dashboard;
using Application.Abstractions.Time;
using Domain.Tags;

namespace Application.Services;

public sealed class DashboardService
{
    private readonly TagService _tagService;
    private readonly AlarmService _alarmService;
    private readonly IClock _clock;

    public DashboardService(TagService tagService, AlarmService alarmService, IClock clock)
    {
        _tagService = tagService;
        _alarmService = alarmService;
        _clock = clock;
    }

    public DashboardSnapshotDto GetSnapshot()
    {
        var tagSnapshot = _tagService.GetSnapshot();
        var alarms = _alarmService.GetCurrentAlarms();

        return BuildSnapshot(tagSnapshot, alarms, _clock.UtcNow);
    }

    public DashboardSnapshotDto BuildSnapshot(
        TagSnapshot tagSnapshot,
        IReadOnlyList<Domain.Alarms.AlarmEvent> currentAlarms,
        DateTime capturedAt)
        => BuildSnapshot(tagSnapshot.CurrentValues, currentAlarms, capturedAt);

    public DashboardSnapshotDto BuildSnapshot(
        IReadOnlyList<TagRuntimeState> currentValues,
        IReadOnlyList<Domain.Alarms.AlarmEvent> currentAlarms,
        DateTime capturedAt)
    {
        var latestState = currentValues
            .OrderByDescending(tag => tag.SequenceNo)
            .FirstOrDefault();

        return new DashboardSnapshotDto(
            capturedAt,
            currentValues,
            currentAlarms,
            currentValues.Count,
            currentValues.Count(tag => tag.Quality != TagQuality.Good),
            latestState?.SourceFrameId ?? Guid.Empty,
            latestState?.SequenceNo ?? 0);
    }
}
