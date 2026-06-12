using Application.DTOs.UI;
using Application.Abstractions.Time;
using Application.Caches;

namespace Application.Services;

public sealed class UiSnapshotProvider
{
    private readonly TagService _tagService;
    private readonly AlarmService _alarmService;
    private readonly DashboardService _dashboardService;
    private readonly ChartDataService _chartDataService;
    private readonly MeasurementMapService _measurementMapService;
    private readonly IClock _clock;
    private readonly ProcessedFrameSnapshotStore _processedFrameStore;

    public UiSnapshotProvider(
        TagService tagService,
        AlarmService alarmService,
        DashboardService dashboardService,
        ChartDataService chartDataService,
        MeasurementMapService measurementMapService,
        IClock clock,
        ProcessedFrameSnapshotStore processedFrameStore)
    {
        _tagService = tagService;
        _alarmService = alarmService;
        _dashboardService = dashboardService;
        _chartDataService = chartDataService;
        _measurementMapService = measurementMapService;
        _clock = clock;
        _processedFrameStore = processedFrameStore ?? throw new ArgumentNullException(nameof(processedFrameStore));
    }

    public UiCompositeSnapshotDto GetSnapshot(UiSnapshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var capturedAt = _clock.UtcNow;
        var tagSnapshot = _tagService.GetSnapshot();
        var processedFrame = _processedFrameStore.GetLatest();
        var currentValues = processedFrame?.TagRuntimeStates ?? [];
        var alarmSnapshot = _alarmService.GetSnapshot();
        var dashboard = _dashboardService.BuildSnapshot(
            currentValues,
            alarmSnapshot.CurrentAlarms,
            capturedAt);
        var dashboardTrend = _chartDataService.BuildTrendSeries(
            tagSnapshot,
            request.DashboardTrendTagId,
            request.DashboardTrendPointCount,
            currentValues,
            processedFrame?.TimestampUtc);
        var selectedTrend = _chartDataService.BuildTrendSnapshot(
            tagSnapshot,
            request.SelectedTrendTagId,
            request.SelectedTrendWindow,
            request.SelectedTrendPointCount,
            capturedAt,
            currentValues,
            processedFrame?.TimestampUtc);

        var analysis = request.IncludeMatrix
            ? processedFrame?.MatrixAnalysis
            : null;
        var measurementMap = analysis is null
            ? null
            : _measurementMapService.BuildMeasurementMapSnapshot(
                analysis,
                request.MatrixDisplayOptions);
        var matrixPreview = analysis is null
            ? null
            : _measurementMapService.BuildMatrixPreview(analysis);
        const bool isFrameConsistent = true;

        return new UiCompositeSnapshotDto(
            capturedAt,
            dashboard,
            dashboardTrend,
            selectedTrend,
            alarmSnapshot,
            analysis,
            measurementMap,
            matrixPreview,
            isFrameConsistent);
    }
}
