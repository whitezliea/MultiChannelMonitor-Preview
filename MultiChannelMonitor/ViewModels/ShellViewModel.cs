using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Application.Configuration;
using Application.DTOs.UI;
using Application.Services;
using Domain.Tags;
using Presentation.Wpf.Bootstrap;
using Presentation.Wpf.Models;
using Presentation.Wpf.Navigation;
using Presentation.Wpf.Services;
using AppLogging;

namespace Presentation.Wpf.ViewModels;

public sealed partial class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IRuntimeOptionsStore _runtimeOptionsStore;
    private readonly ConfigurationService _configurationService;
    private readonly UiSnapshotProvider _uiSnapshotProvider;
    private readonly RuntimeLifecycleCoordinator _runtimeLifecycle;
    private readonly PersistenceRuntimeCoordinator _persistenceRuntime;
    private readonly ApplicationRuntimeHost _applicationRuntime;
    private readonly AcquisitionRuntimeController _acquisitionRuntime;
    private readonly DataSourceHealthMonitor _dataSourceHealthMonitor;
    private readonly UiDispatcherService _uiDispatcher;
    private readonly DispatcherTimer _uiRefreshTimer = new();
    private readonly IReadOnlyDictionary<string, TagDefinition> _definitionMap;
    private bool _disposed;

    public ShellViewModel(RuntimeComposition composition, UiDispatcherService uiDispatcher)
    {
        _runtimeOptionsStore = composition.RuntimeOptionsStore;
        _configurationService = composition.ConfigurationService;
        _definitionMap = composition.DefinitionMap;
        _uiSnapshotProvider = composition.UiSnapshotProvider;
        _runtimeLifecycle = composition.RuntimeLifecycle;
        _persistenceRuntime = composition.PersistenceRuntime;
        _applicationRuntime = composition.ApplicationRuntime;
        _acquisitionRuntime = composition.AcquisitionRuntime;
        _dataSourceHealthMonitor = composition.DataSourceHealthMonitor;
        _uiDispatcher = uiDispatcher;
        _runtimeLifecycle.StatusChanged += OnRuntimeStatusChanged;
        _dataSourceHealthMonitor.StatusChanged += OnDataSourceHealthStatusChanged;
        _persistenceRuntime.StatusChanged += OnPersistenceStatusChanged;
        _configurationService.RuntimeOptionsChanged += OnRuntimeOptionsChanged;

        Dashboard = new DashboardViewModel(() => NavigateTo(NavigationPage.MeasurementMap));
        RealtimeTags = new RealtimeTagsViewModel(_definitionMap, OpenTrend);
        Trend = new TrendViewModel(
            composition.TagDefinitions,
            _runtimeOptionsStore.Snapshot,
            OpenHistory);
        AlarmCenter = new AlarmCenterViewModel(
            composition.AcknowledgeAlarmUseCase,
            composition.QueryAlarmsUseCase);
        History = new HistoryViewModel(
            composition.TagDefinitions,
            composition.QueryHistorySamplesUseCase,
            composition.ExportHistoryCsvUseCase,
            new FilePickerService(),
            composition.TagRuntimeConfigurationStore);
        MeasurementMap = new MeasurementMapViewModel();
        LogsSettings = new LogsSettingsViewModel(
            composition.TagDefinitions,
            composition.QueryOperationLogsUseCase,
            composition.SaveTagRuntimeSettingsUseCase,
            composition.SaveRuntimeSettingsUseCase,
            composition.TagRuntimeConfigurationStore,
            composition.RuntimeOptionsStore);

        NavigationItems =
        [
            new NavigationItemModel(NavigationPage.Dashboard, "Dashboard", "D"),
            new NavigationItemModel(NavigationPage.RealtimeTags, "Realtime Tags", "T"),
            new NavigationItemModel(NavigationPage.Trend, "Trend", "R"),
            new NavigationItemModel(NavigationPage.AlarmCenter, "Alarm Center", "A"),
            new NavigationItemModel(NavigationPage.History, "History", "H"),
            new NavigationItemModel(NavigationPage.MeasurementMap, "Matrix Map", "M"),
            new NavigationItemModel(NavigationPage.LogsSettings, "Logs / Settings", "L")
        ];

        CurrentViewModel = Dashboard;
        CurrentPageTitle = Dashboard.Title;
        _uiRefreshTimer.Interval = _runtimeOptionsStore.Snapshot.UiRefreshInterval;
        _uiRefreshTimer.Tick += (_, _) => RefreshPages();
        _uiRefreshTimer.Start();
    }

    public DashboardViewModel Dashboard { get; }
    public RealtimeTagsViewModel RealtimeTags { get; }
    public TrendViewModel Trend { get; }
    public AlarmCenterViewModel AlarmCenter { get; }
    public HistoryViewModel History { get; }
    public MeasurementMapViewModel MeasurementMap { get; }
    public LogsSettingsViewModel LogsSettings { get; }
    public ObservableCollection<NavigationItemModel> NavigationItems { get; }

    [ObservableProperty]
    private PageViewModelBase currentViewModel;

    [ObservableProperty]
    private string currentPageTitle = "";

    [ObservableProperty]
    private string dataSourceStatus = "Stopped";

    [ObservableProperty]
    private string persistenceStatus = "Stopped";

    [ObservableProperty]
    private string statusText = "DataSource: Stopped | Last Frame: -- | UI Refresh: 1s";

    [ObservableProperty]
    private string clockText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    public string HeaderSubtitle => "Instrument monitoring shell driven by realtime tags, alarms, trends and matrix views";

    [RelayCommand]
    private void Navigate(NavigationItemModel item) => NavigateTo(item.Page);

    public Task InitializeAsync() => _applicationRuntime.StartAsync();

    private void NavigateTo(NavigationPage page)
    {
        CurrentViewModel = page switch
        {
            NavigationPage.RealtimeTags => RealtimeTags,
            NavigationPage.Trend => Trend,
            NavigationPage.AlarmCenter => AlarmCenter,
            NavigationPage.History => History,
            NavigationPage.MeasurementMap => MeasurementMap,
            NavigationPage.LogsSettings => LogsSettings,
            _ => Dashboard
        };
        CurrentPageTitle = CurrentViewModel.Title;
    }

    private void OpenTrend(string tagId)
    {
        if (!Trend.AvailableTags.Contains(tagId, StringComparer.Ordinal))
        {
            return;
        }

        Trend.SelectedTagId = tagId;
        NavigateTo(NavigationPage.Trend);
    }

    private void OpenHistory(string tagId, TimeSpan trendWindow)
    {
        var historyRange = trendWindow < TimeSpan.FromMinutes(30)
            ? TimeSpan.FromMinutes(30)
            : trendWindow;
        History.ApplyNavigationContext(tagId, historyRange);
        NavigateTo(NavigationPage.History);
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (!await _acquisitionRuntime.StartAsync())
        {
            return;
        }

        AppLogger.Info("Simulator started!!!");
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (await _acquisitionRuntime.StopAsync())
        {
            AppLogger.Info("Simulator stopped.");
        }
    }

    private void RefreshPages()
    {
        var snapshot = _uiSnapshotProvider.GetSnapshot(new UiSnapshotRequest(
            "MEAS.TEMP.CH01",
            18,
            Trend.SelectedTagId,
            Trend.WindowDuration,
            Trend.WindowPointCount,
            MeasurementMap.DisplayOptions,
            true));
        var lastFrame = snapshot.Dashboard.SequenceNo <= 0
            ? "--"
            : snapshot.Dashboard.SequenceNo.ToString();

        Dashboard.Refresh(
            snapshot.Dashboard,
            _definitionMap,
            snapshot.DashboardTrend,
            snapshot.MatrixPreview);
        var dataFreshness = GetDataFreshnessText();
        RealtimeTags.Refresh(snapshot.Dashboard.Tags, dataFreshness);
        Trend.Refresh(snapshot.SelectedTrend, dataFreshness);
        AlarmCenter.Refresh(snapshot.AlarmCenter);
        MeasurementMap.Refresh(snapshot.MeasurementMap, dataFreshness);

        ClockText = snapshot.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        var matrixFrame = snapshot.MatrixAnalysis?.SequenceNo > 0
            ? snapshot.MatrixAnalysis.SequenceNo.ToString()
            : "--";
        var frameSync = snapshot.IsFrameConsistent ? "Synced" : "Mixed";
        StatusText = $"Data: {DataSourceStatus} | DB: {PersistenceStatus} | Tag: {lastFrame} | Matrix: {matrixFrame} | Sync: {frameSync} | UI: {_runtimeOptionsStore.Snapshot.UiRefreshInterval.TotalSeconds:0}s";

        //AppLogger.Info("UI Refresh!!!");
    }

    private async void OnRuntimeStatusChanged(object? sender, RuntimeLifecycleStatus status)
    {
        try
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                DataSourceStatus = status.State switch
                {
                    RuntimeLifecycleState.Running => GetHealthStatusText(_dataSourceHealthMonitor.Status.State),
                    _ => status.State.ToString()
                };
                if (status.State == RuntimeLifecycleState.Faulted && status.Error is not null)
                {
                    AppLogger.Error(status.Error, "Monitoring runtime stopped unexpectedly.");
                }
            });
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async void OnDataSourceHealthStatusChanged(object? sender, DataSourceHealthStatus status)
    {
        try
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                if (_runtimeLifecycle.Status.State is RuntimeLifecycleState.Stopped or RuntimeLifecycleState.Stopping)
                {
                    DataSourceStatus = "Stopped";
                    return;
                }

                DataSourceStatus = GetHealthStatusText(status.State);
            });
        }
        catch (TaskCanceledException)
        {
        }
    }

    private string GetDataFreshnessText()
    {
        if (_runtimeLifecycle.Status.State is RuntimeLifecycleState.Stopped or RuntimeLifecycleState.Stopping)
        {
            return "Stale (Acquisition Stopped)";
        }

        return _dataSourceHealthMonitor.Status.State switch
        {
            DataSourceHealthState.TimedOut => "Offline",
            DataSourceHealthState.Online => "Live",
            _ => "Waiting"
        };
    }

    private static string GetHealthStatusText(DataSourceHealthState state) => state switch
    {
        DataSourceHealthState.WaitingForFirstFrame => "Waiting",
        DataSourceHealthState.Online => "Online",
        DataSourceHealthState.TimedOut => "TimedOut",
        _ => "Stopped"
    };

    private async void OnPersistenceStatusChanged(object? sender, PersistenceRuntimeStatus status)
    {
        try
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                PersistenceStatus = status.State.ToString();
                if (status.State is not (PersistenceRuntimeState.Degraded or PersistenceRuntimeState.Faulted))
                {
                    return;
                }

                AppLogger.Error(
                    status.Error ?? new InvalidOperationException("Persistence health degraded."),
                    "{0} persistence is {1}.",
                    status.WorkerName ?? "Unknown",
                    status.State);
            });
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async void OnRuntimeOptionsChanged(object? sender, MonitorRuntimeOptions options)
    {
        try
        {
            await _uiDispatcher.InvokeAsync(() => _uiRefreshTimer.Interval = options.UiRefreshInterval);
        }
        catch (TaskCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _uiRefreshTimer.Stop();
        _runtimeLifecycle.StatusChanged -= OnRuntimeStatusChanged;
        _dataSourceHealthMonitor.StatusChanged -= OnDataSourceHealthStatusChanged;
        _persistenceRuntime.StatusChanged -= OnPersistenceStatusChanged;
        _configurationService.RuntimeOptionsChanged -= OnRuntimeOptionsChanged;
        History.Dispose();
        AlarmCenter.Dispose();
        await _runtimeLifecycle.DisposeAsync();
        await _applicationRuntime.DisposeAsync();
    }
}
