using System.Collections.ObjectModel;
using Application.DTOs.Charts;
using Application.DTOs.Dashboard;
using Application.DTOs.MeasurementMap;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Domain.Alarms;
using Domain.Tags;
using Presentation.Wpf.Models;
using Presentation.Wpf.Renderers;

namespace Presentation.Wpf.ViewModels;

public sealed partial class DashboardViewModel : PageViewModelBase
{
    private readonly HeatmapRenderer _heatmapRenderer;
    private readonly Action _openMeasurementMap;

    public DashboardViewModel()
        : this(new HeatmapRenderer(), static () => { })
    {
    }

    internal DashboardViewModel(Action openMeasurementMap)
        : this(new HeatmapRenderer(), openMeasurementMap)
    {
    }

    internal DashboardViewModel(HeatmapRenderer heatmapRenderer, Action openMeasurementMap) : base("Dashboard")
    {
        _heatmapRenderer = heatmapRenderer ?? throw new ArgumentNullException(nameof(heatmapRenderer));
        _openMeasurementMap = openMeasurementMap ?? throw new ArgumentNullException(nameof(openMeasurementMap));
        Metrics =
        [
            new MetricCardModel { Title = "Device", Value = "Stopped", Subtitle = "MCMD-001" },
            new MetricCardModel { Title = "Active Alarm", Value = "0", Subtitle = "Current active/ack" },
            new MetricCardModel { Title = "Sample Rate", Value = "500 ms", Subtitle = "Simulator interval" },
            new MetricCardModel { Title = "Data Quality", Value = "--", Subtitle = "Good tag ratio" }
        ];
    }

    public ObservableCollection<MetricCardModel> Metrics { get; }
    public ObservableCollection<RealtimeTagModel> KeyMeasurements { get; } = [];
    public ObservableCollection<AlarmItemModel> ActiveAlarms { get; } = [];

    [ObservableProperty]
    private TrendSeriesDto? currentTrendSeries;

    [ObservableProperty]
    private bool hasTrendPreview;

    [ObservableProperty]
    private string trendWindowText = "Window: Waiting for data";

    [ObservableProperty]
    private IReadOnlyList<MatrixPreviewCellModel> matrixPreviewCells = [];

    [ObservableProperty]
    private int matrixPreviewColumns = 1;

    [ObservableProperty]
    private bool hasMatrixPreview;

    [ObservableProperty]
    private MatrixQualityState? matrixQualityState;

    [ObservableProperty]
    private string matrixQualityText = "No Data";

    [ObservableProperty]
    private string matrixMaximumText = "--";

    [ObservableProperty]
    private string matrixAverageText = "--";

    [ObservableProperty]
    private string matrixUniformityText = "--";

    [ObservableProperty]
    private string matrixAbnormalCountText = "--";

    [ObservableProperty]
    private string mainAbnormalPointText = "--";

    [ObservableProperty]
    private string mainAbnormalTypeText = "--";

    [ObservableProperty]
    private string matrixTimestampText = "--";

    public void Refresh(
        DashboardSnapshotDto snapshot,
        IReadOnlyDictionary<string, TagDefinition> definitions,
        TrendSeriesDto trendSeries,
        MatrixPreviewDto? matrixPreview)
    {
        Metrics[0].Value = snapshot.TotalTagCount > 0 ? "Running" : "Stopped";
        Metrics[1].Value = snapshot.ActiveAlarms.Count.ToString();
        Metrics[3].Value = snapshot.TotalTagCount == 0
            ? "--"
            : $"{(snapshot.TotalTagCount - snapshot.BadQualityCount) * 100d / snapshot.TotalTagCount:0.0}% Good";

        Replace(KeyMeasurements, snapshot.Tags.Take(6).Select(tag => ToTagModel(tag, definitions)));
        Replace(ActiveAlarms, snapshot.ActiveAlarms.Take(4).Select(ToAlarmModel));
        CurrentTrendSeries = trendSeries;
        HasTrendPreview = trendSeries.Points.Any(point => double.IsFinite(point.Value));
        TrendWindowText = trendSeries.RequestedPointCount > 0
            ? $"Window: Latest {trendSeries.RequestedPointCount} samples ({trendSeries.Points.Count} received)"
            : $"Window: {trendSeries.Points.Count} samples";

        ApplyMatrixPreview(matrixPreview);
    }

    [RelayCommand]
    private void OpenMeasurementMap() => _openMeasurementMap();

    internal static RealtimeTagModel ToTagModel(TagRuntimeState tag, IReadOnlyDictionary<string, TagDefinition> definitions)
    {
        definitions.TryGetValue(tag.TagId, out var definition);
        return new RealtimeTagModel
        {
            TagId = tag.TagId,
            DisplayName = definition?.DisplayName ?? tag.TagId,
            Category = definition?.Category.ToString() ?? "Unknown",
            Value = tag.NumericValue ?? (tag.BoolValue.HasValue ? tag.BoolValue.Value ? 1d : 0d : 0d),
            DisplayValue = FormatDisplayValue(tag),
            Unit = definition?.Unit ?? "",
            Quality = tag.Quality,
            AlarmState = tag.AlarmState,
            Timestamp = tag.Timestamp.ToLocalTime(),
            SequenceNo = tag.SequenceNo
        };
    }

    internal static AlarmItemModel ToAlarmModel(AlarmEvent alarm) => new()
    {
        AlarmId = alarm.AlarmId,
        TagId = alarm.TagId,
        Level = alarm.Level,
        State = alarm.State,
        AlarmType = alarm.AlarmType,
        TriggerValue = alarm.TriggerValue,
        TriggerTime = alarm.TriggerTime.ToLocalTime(),
        AcknowledgeTime = alarm.AcknowledgeTime?.ToLocalTime(),
        RecoverTime = alarm.RecoverTime?.ToLocalTime(),
        LastUpdatedTime = alarm.LastUpdatedTime?.ToLocalTime(),
        CloseReason = alarm.CloseReason ?? "",
        Message = alarm.Message
    };

    private void ApplyMatrixPreview(MatrixPreviewDto? preview)
    {
        if (preview is null)
        {
            ClearMatrixPreview();
            return;
        }

        MatrixPreviewCells = preview.Cells
            .Select(_heatmapRenderer.CreatePreviewCellModel)
            .ToArray();
        MatrixPreviewColumns = Math.Max(1, preview.Columns);
        HasMatrixPreview = true;
        MatrixQualityState = preview.QualityState;
        MatrixQualityText = preview.QualityState.ToString();
        MatrixMaximumText = FormatMatrixValue(preview.Maximum, preview.Unit);
        MatrixAverageText = FormatMatrixValue(preview.Average, preview.Unit);
        MatrixUniformityText = double.IsFinite(preview.Uniformity)
            ? preview.Uniformity.ToString("P1")
            : "--";
        MatrixAbnormalCountText = preview.AbnormalCount.ToString();
        MainAbnormalPointText = preview.MainAbnormalPoint is null
            ? "--"
            : $"R{preview.MainAbnormalPoint.Row:00} / C{preview.MainAbnormalPoint.Column:00}";
        MainAbnormalTypeText = preview.MainAbnormalPoint?.Type.ToString() ?? "--";
        MatrixTimestampText = preview.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    private void ClearMatrixPreview()
    {
        MatrixPreviewCells = [];
        MatrixPreviewColumns = 1;
        HasMatrixPreview = false;
        MatrixQualityState = null;
        MatrixQualityText = "No Data";
        MatrixMaximumText = "--";
        MatrixAverageText = "--";
        MatrixUniformityText = "--";
        MatrixAbnormalCountText = "--";
        MainAbnormalPointText = "--";
        MainAbnormalTypeText = "--";
        MatrixTimestampText = "--";
    }

    private static string FormatMatrixValue(double value, string unit) =>
        double.IsFinite(value) ? $"{value:0.###} {unit}" : "--";

    private static string FormatDisplayValue(TagRuntimeState tag)
    {
        if (tag.NumericValue.HasValue)
        {
            return tag.NumericValue.Value.ToString("0.###");
        }

        if (tag.BoolValue.HasValue)
        {
            return tag.BoolValue.Value ? "True" : "False";
        }

        return tag.TextValue ?? "";
    }

    internal static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}
