using Application.DTOs.Alarms;
using Application.DTOs.Charts;
using Application.DTOs.Dashboard;
using Application.DTOs.MeasurementMap;

namespace Application.DTOs.UI;

public sealed record UiSnapshotRequest(
    string DashboardTrendTagId,
    int DashboardTrendPointCount,
    string SelectedTrendTagId,
    TimeSpan SelectedTrendWindow,
    int SelectedTrendPointCount,
    MatrixDisplayOptionsDto MatrixDisplayOptions,
    bool IncludeMatrix);

public sealed record UiCompositeSnapshotDto(
    DateTime CapturedAt,
    DashboardSnapshotDto Dashboard,
    TrendSeriesDto DashboardTrend,
    TrendSnapshotDto SelectedTrend,
    AlarmCenterSnapshotDto AlarmCenter,
    MatrixAnalysisSnapshotDto? MatrixAnalysis,
    MeasurementMapSnapshotDto? MeasurementMap,
    MatrixPreviewDto? MatrixPreview,
    bool IsFrameConsistent);
