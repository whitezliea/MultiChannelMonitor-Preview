using System.Collections.ObjectModel;
using Application.Configuration;
using Application.DTOs.Charts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Domain.Tags;
using Presentation.Wpf.Models;

namespace Presentation.Wpf.ViewModels;

public sealed partial class TrendViewModel : PageViewModelBase
{
    private const int RecentSampleLimit = 50;
    private readonly IReadOnlyDictionary<string, int> _windowPointCounts;
    private readonly IReadOnlyDictionary<string, TimeSpan> _windowDurations;
    private readonly IReadOnlySet<string> _historyTagIds;
    private readonly Action<string, TimeSpan> _openHistory;
    private TrendSnapshotDto? _pendingSnapshot;
    private string _pendingDataStatus = "Waiting";

    public TrendViewModel(
        IReadOnlyList<TagDefinition> definitions,
        MonitorRuntimeOptions options,
        Action<string, TimeSpan>? openHistory = null) : base("Trend")
    {
        _openHistory = openHistory ?? ((_, _) => { });
        _historyTagIds = definitions
            .Where(item => item.IsEnabled
                && item.IsHistorized
                && item.DataType is TagDataType.Double or TagDataType.Int or TagDataType.Number)
            .Select(item => item.TagId)
            .ToHashSet(StringComparer.Ordinal);
        AvailableTags = definitions
            .Where(item => item.IsEnabled
                && item.DataType is TagDataType.Double or TagDataType.Int or TagDataType.Number)
            .OrderBy(item => item.DisplayOrder)
            .Select(item => item.TagId)
            .ToArray();
        var windows = options.TrendWindows
            .Distinct()
            .OrderBy(window => window)
            .ToArray();
        _windowPointCounts = windows.ToDictionary(
            FormatWindow,
            options.GetTrendPointCount,
            StringComparer.Ordinal);
        _windowDurations = windows.ToDictionary(
            FormatWindow,
            window => window,
            StringComparer.Ordinal);
        TimeWindows = _windowPointCounts.Keys.ToArray();
        SelectedTagId = AvailableTags.FirstOrDefault() ?? "";
        SelectedTimeWindow = TimeWindows.FirstOrDefault() ?? "";
        ResetDisplayedState("Waiting for the first trend snapshot.");
    }

    public IReadOnlyList<string> AvailableTags { get; }
    public IReadOnlyList<string> TimeWindows { get; }
    public ObservableCollection<TrendPointModel> Points { get; } = [];

    [ObservableProperty]
    private string selectedTagId = "";

    [ObservableProperty]
    private string selectedTimeWindow = "";

    [ObservableProperty]
    private TrendSnapshotDto? currentSnapshot;

    [ObservableProperty]
    private bool isAutoRefreshEnabled = true;

    [ObservableProperty]
    private string currentValueText = "--";

    [ObservableProperty]
    private TagQuality? currentQuality;

    [ObservableProperty]
    private TagAlarmState? currentAlarmState;

    [ObservableProperty]
    private string currentTimestampText = "--";

    [ObservableProperty]
    private string collectionStatusText = "Waiting";

    [ObservableProperty]
    private string dataStatusText = "Waiting";

    [ObservableProperty]
    private string statusMessage = "Waiting for the first trend snapshot.";

    [ObservableProperty]
    private string lastText = "--";

    [ObservableProperty]
    private string minimumText = "--";

    [ObservableProperty]
    private string maximumText = "--";

    [ObservableProperty]
    private string averageText = "--";

    [ObservableProperty]
    private string stdDevText = "--";

    [ObservableProperty]
    private string pointsText = "0";

    [ObservableProperty]
    private string diagnosisStateText = "InsufficientData";

    [ObservableProperty]
    private string spikeText = "0";

    [ObservableProperty]
    private string driftText = "--";

    [ObservableProperty]
    private string diagnosisMessage = "Waiting for trend diagnosis.";

    [ObservableProperty]
    private bool isOverlayVisible = true;

    [ObservableProperty]
    private string overlayText = "Waiting for the first trend snapshot.";

    [ObservableProperty]
    private string statisticsText = "No data";

    public bool IsPaused => !IsAutoRefreshEnabled;

    public int WindowPointCount =>
        _windowPointCounts.TryGetValue(SelectedTimeWindow, out var pointCount)
            ? pointCount
            : 0;

    public TimeSpan WindowDuration =>
        _windowDurations.TryGetValue(SelectedTimeWindow, out var window)
            ? window
            : TimeSpan.Zero;

    public void Refresh(TrendSnapshotDto snapshot, string dataStatus = "Live")
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!MatchesCurrentSelection(snapshot))
        {
            return;
        }

        if (!IsAutoRefreshEnabled)
        {
            _pendingSnapshot = snapshot;
            _pendingDataStatus = dataStatus;
            DataStatusText = "Paused";
            StatusMessage = "Auto refresh is paused. The latest snapshot will be applied when resumed.";
            if (IsOverlayVisible)
            {
                OverlayText = StatusMessage;
            }
            return;
        }

        ApplySnapshot(snapshot, dataStatus);
    }

    partial void OnIsAutoRefreshEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPaused));
        if (!value)
        {
            DataStatusText = "Paused";
            StatusMessage = "Auto refresh is paused. The current chart is frozen.";
            if (IsOverlayVisible)
            {
                OverlayText = StatusMessage;
            }
            return;
        }

        if (_pendingSnapshot is { } pendingSnapshot)
        {
            var pendingStatus = _pendingDataStatus;
            _pendingSnapshot = null;
            ApplySnapshot(pendingSnapshot, pendingStatus);
            return;
        }

        if (CurrentSnapshot is { } current)
        {
            ApplySnapshot(current, _pendingDataStatus);
        }
        else
        {
            ResetDisplayedState("Waiting for the next trend snapshot.");
        }
    }

    partial void OnSelectedTagIdChanged(string value) =>
        HandleSelectionChange();

    partial void OnSelectedTimeWindowChanged(string value) =>
        HandleSelectionChange();

    [RelayCommand(CanExecute = nameof(CanOpenHistory))]
    private void OpenHistory()
    {
        if (!CanOpenHistory())
        {
            return;
        }

        _openHistory(SelectedTagId, WindowDuration);
    }

    private void ApplySnapshot(TrendSnapshotDto snapshot, string dataStatus)
    {
        CurrentSnapshot = snapshot;
        _pendingSnapshot = null;
        _pendingDataStatus = dataStatus;
        DataStatusText = NormalizeDataStatus(dataStatus, snapshot.CurrentQuality);
        CurrentValueText = FormatValue(snapshot.CurrentValue, snapshot.Metadata.Unit);
        CurrentQuality = snapshot.CurrentQuality;
        CurrentAlarmState = snapshot.CurrentAlarmState;
        CurrentTimestampText = snapshot.CurrentTimestamp?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "--";

        var statistics = snapshot.Statistics;
        LastText = FormatValue(statistics.Last, snapshot.Metadata.Unit);
        MinimumText = FormatValue(statistics.Minimum, snapshot.Metadata.Unit);
        MaximumText = FormatValue(statistics.Maximum, snapshot.Metadata.Unit);
        AverageText = FormatValue(statistics.Average, snapshot.Metadata.Unit);
        StdDevText = FormatValue(statistics.StdDev, snapshot.Metadata.Unit);
        PointsText = $"{statistics.ValidCount} / {statistics.TotalCount}";
        DiagnosisStateText = snapshot.Diagnosis.State.ToString();
        SpikeText = snapshot.Diagnosis.SpikeCount.ToString();
        DriftText = FormatDrift(snapshot.Diagnosis.NormalizedSlopePercentPerMinute);
        DiagnosisMessage = snapshot.Diagnosis.Message;
        CollectionStatusText = BuildCollectionStatus(snapshot);
        StatisticsText = statistics.ValidCount == 0
            ? $"No valid data | {CollectionStatusText}"
            : $"Min {statistics.Minimum:0.###} / Max {statistics.Maximum:0.###} / Avg {statistics.Average:0.###} / StdDev {statistics.StdDev:0.###} | {CollectionStatusText}";

        DashboardViewModel.Replace(
            Points,
            snapshot.Series.Points
                .TakeLast(RecentSampleLimit)
                .Select(point => new TrendPointModel
                {
                    TimeText = point.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                    Value = point.Value
                }));

        UpdateStatus(snapshot, dataStatus);
    }

    private void UpdateStatus(TrendSnapshotDto snapshot, string dataStatus)
    {
        if (dataStatus.StartsWith("Stale", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(
                snapshot.Series.Points.Count == 0
                    ? "Acquisition is stopped and no trend samples are available."
                    : "Acquisition is stopped. Showing the last received trend window.",
                snapshot.Series.Points.Count == 0);
            return;
        }

        if (string.Equals(dataStatus, "Offline", StringComparison.OrdinalIgnoreCase)
            || snapshot.CurrentQuality == TagQuality.Offline)
        {
            SetStatus(
                snapshot.Series.Points.Count == 0
                    ? "Data source is offline and no trend samples are available."
                    : "Data source is offline. Showing retained trend samples.",
                snapshot.Series.Points.Count == 0);
            return;
        }

        if (snapshot.Series.Points.Count == 0)
        {
            SetStatus("Waiting for the first sample in the selected window.", true);
            return;
        }

        if (snapshot.Statistics.ValidCount == 0)
        {
            SetStatus("No valid numeric samples are available in the selected window.", true);
            return;
        }

        SetStatus(
            snapshot.IsWindowComplete
                ? "Trend window is complete."
                : $"Collecting trend samples: {snapshot.Series.Points.Count}/{snapshot.ExpectedPointCount}.",
            false);
    }

    private void SetStatus(string message, bool showOverlay)
    {
        StatusMessage = message;
        OverlayText = message;
        IsOverlayVisible = showOverlay;
    }

    private void ResetForSelectionChange()
    {
        _pendingSnapshot = null;
        CurrentSnapshot = null;
        ResetDisplayedState(
            string.IsNullOrWhiteSpace(SelectedTagId)
                ? "No numeric Tag is available for trend display."
                : "Waiting for data for the selected Tag and window.");
    }

    private void HandleSelectionChange()
    {
        ResetForSelectionChange();
        OpenHistoryCommand.NotifyCanExecuteChanged();
    }

    private bool CanOpenHistory() =>
        !string.IsNullOrWhiteSpace(SelectedTagId)
        && _historyTagIds.Contains(SelectedTagId)
        && WindowDuration > TimeSpan.Zero;

    private void ResetDisplayedState(string message)
    {
        Points.Clear();
        CurrentValueText = "--";
        CurrentQuality = null;
        CurrentAlarmState = null;
        CurrentTimestampText = "--";
        CollectionStatusText = "Waiting";
        DataStatusText = IsAutoRefreshEnabled ? "Waiting" : "Paused";
        LastText = "--";
        MinimumText = "--";
        MaximumText = "--";
        AverageText = "--";
        StdDevText = "--";
        PointsText = "0 / 0";
        DiagnosisStateText = "InsufficientData";
        SpikeText = "0";
        DriftText = "--";
        DiagnosisMessage = "Waiting for trend diagnosis.";
        StatisticsText = "No data";
        SetStatus(message, true);
    }

    private bool MatchesCurrentSelection(TrendSnapshotDto snapshot) =>
        string.Equals(snapshot.TagId, SelectedTagId, StringComparison.Ordinal)
        && snapshot.Window == WindowDuration;

    private static string BuildCollectionStatus(TrendSnapshotDto snapshot)
    {
        if (snapshot.ExpectedPointCount <= 0)
        {
            return $"{snapshot.Series.Points.Count} points";
        }

        return snapshot.IsWindowComplete
            ? $"Complete: {snapshot.Series.Points.Count}/{snapshot.ExpectedPointCount}"
            : $"Collecting: {snapshot.Series.Points.Count}/{snapshot.ExpectedPointCount}";
    }

    private static string NormalizeDataStatus(string dataStatus, TagQuality? quality)
    {
        if (quality == TagQuality.Offline
            || string.Equals(dataStatus, "Offline", StringComparison.OrdinalIgnoreCase))
        {
            return "Offline";
        }

        if (dataStatus.StartsWith("Stale", StringComparison.OrdinalIgnoreCase))
        {
            return "Stopped";
        }

        return dataStatus;
    }

    private static string FormatValue(double? value, string unit)
    {
        if (!value.HasValue || !double.IsFinite(value.Value))
        {
            return "--";
        }

        return string.IsNullOrWhiteSpace(unit)
            ? value.Value.ToString("0.###")
            : $"{value.Value:0.###} {unit}";
    }

    private static string FormatDrift(double? normalizedSlope)
    {
        if (!normalizedSlope.HasValue || !double.IsFinite(normalizedSlope.Value))
        {
            return "--";
        }

        var direction = normalizedSlope.Value >= 0 ? "Rising" : "Falling";
        return $"{direction} {Math.Abs(normalizedSlope.Value):0.###}% span/min";
    }

    private static string FormatWindow(TimeSpan window) =>
        window.TotalMinutes >= 1 && window.TotalMinutes == Math.Truncate(window.TotalMinutes)
            ? $"{window.TotalMinutes:0} min"
            : $"{window.TotalSeconds:0.###} sec";
}
