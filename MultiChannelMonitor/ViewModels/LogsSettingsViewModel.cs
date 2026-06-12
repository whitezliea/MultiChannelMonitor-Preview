using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Application.UseCases.Logs;
using Application.UseCases.Settings;
using Application.Configuration;
using Domain.Logs;
using Domain.Tags;
using Presentation.Wpf.Models;

namespace Presentation.Wpf.ViewModels;

public sealed partial class LogsSettingsViewModel : PageViewModelBase
{
    private readonly QueryOperationLogsUseCase _queryOperationLogsUseCase;
    private readonly SaveTagRuntimeSettingsUseCase _saveTagRuntimeSettingsUseCase;
    private readonly SaveRuntimeSettingsUseCase _saveRuntimeSettingsUseCase;
    private readonly ITagRuntimeConfigurationStore _tagConfigurationStore;
    private readonly IRuntimeOptionsStore _runtimeOptionsStore;

    public LogsSettingsViewModel(
        IReadOnlyList<TagDefinition> definitions,
        QueryOperationLogsUseCase queryOperationLogsUseCase,
        SaveTagRuntimeSettingsUseCase saveTagRuntimeSettingsUseCase,
        SaveRuntimeSettingsUseCase saveRuntimeSettingsUseCase,
        ITagRuntimeConfigurationStore tagConfigurationStore,
        IRuntimeOptionsStore runtimeOptionsStore) : base("Logs & Settings")
    {
        _queryOperationLogsUseCase = queryOperationLogsUseCase;
        _saveTagRuntimeSettingsUseCase = saveTagRuntimeSettingsUseCase;
        _saveRuntimeSettingsUseCase = saveRuntimeSettingsUseCase;
        _tagConfigurationStore = tagConfigurationStore;
        _runtimeOptionsStore = runtimeOptionsStore;
        foreach (var definition in definitions)
        {
            var configuration = tagConfigurationStore.Get(definition.TagId);
            Thresholds.Add(new ThresholdSettingModel
            {
                TagId = definition.TagId,
                WarningLow = configuration.WarningLow,
                AlarmLow = configuration.AlarmLow,
                WarningHigh = configuration.WarningHigh,
                AlarmHigh = configuration.AlarmHigh,
                AlarmEnabled = configuration.AlarmEnabled,
                IsHistorized = configuration.IsHistorized,
                HistoryIntervalMs = configuration.HistoryIntervalMs
            });
        }

        var options = runtimeOptionsStore.Snapshot;
        RuntimeSettings.Add(new RuntimeSettingModel { Name = RuntimeSettingKeys.DataGenerateIntervalMs, Value = options.DataGenerateInterval.TotalMilliseconds.ToString("0"), Unit = "ms", Effect = "Next acquisition start" });
        RuntimeSettings.Add(new RuntimeSettingModel { Name = RuntimeSettingKeys.DataSourceTimeoutPeriods, Value = options.DataSourceTimeoutPeriods.ToString(), Unit = "periods", Effect = "Next acquisition start" });
        RuntimeSettings.Add(new RuntimeSettingModel { Name = RuntimeSettingKeys.UiRefreshIntervalMs, Value = options.UiRefreshInterval.TotalMilliseconds.ToString("0"), Unit = "ms", Effect = "Immediate" });
        RuntimeSettings.Add(new RuntimeSettingModel { Name = RuntimeSettingKeys.HistoryBatchIntervalMs, Value = options.HistoryBatchInterval.TotalMilliseconds.ToString("0"), Unit = "ms", Effect = "Next application start" });
        RuntimeSettings.Add(new RuntimeSettingModel { Name = RuntimeSettingKeys.HistoryRetentionDays, Value = options.HistoryRetentionDays.ToString(), Unit = "days", Effect = "Next application start" });
        RuntimeSettings.Add(new RuntimeSettingModel { Name = RuntimeSettingKeys.AlarmBatchIntervalMs, Value = options.AlarmBatchInterval.TotalMilliseconds.ToString("0"), Unit = "ms", Effect = "Next application start" });
        RuntimeSettings.Add(new RuntimeSettingModel { Name = RuntimeSettingKeys.OperationLogBatchIntervalMs, Value = options.OperationLogBatchInterval.TotalMilliseconds.ToString("0"), Unit = "ms", Effect = "Next application start" });
        RuntimeSettings.Add(new RuntimeSettingModel { Name = RuntimeSettingKeys.MaximumTrendWindowMinutes, Value = options.MaximumTrendWindow.TotalMinutes.ToString("0.###"), Unit = "min", Effect = "Next application start" });
    }

    public ObservableCollection<OperationLogRowModel> OperationLogs { get; } = [];
    public ObservableCollection<ThresholdSettingModel> Thresholds { get; } = [];
    public ObservableCollection<RuntimeSettingModel> RuntimeSettings { get; } = [];

    public IReadOnlyList<OperationLogLevel?> AvailableLevels { get; } =
        [null, .. Enum.GetValues<OperationLogLevel>().Cast<OperationLogLevel?>()];

    [ObservableProperty]
    private DateTime queryStartLocal = DateTime.Today.AddDays(-1);

    [ObservableProperty]
    private DateTime queryEndLocal = DateTime.Now;

    [ObservableProperty]
    private OperationLogLevel? selectedLevel;

    [ObservableProperty]
    private string categoryFilter = "";

    [ObservableProperty]
    private int maximumResults = 200;

    [ObservableProperty]
    private string queryStatus = "Select filters and query persisted operation logs.";

    [ObservableProperty]
    private bool isQuerying;

    [ObservableProperty]
    private string settingsStatus = "Settings are loaded from SQLite.";

    [RelayCommand]
    private async Task QueryLogsAsync()
    {
        if (QueryStartLocal > QueryEndLocal)
        {
            QueryStatus = "Start time must not be later than end time.";
            return;
        }

        try
        {
            IsQuerying = true;
            QueryStatus = "Querying operation logs...";
            var endLocal = QueryEndLocal.TimeOfDay == TimeSpan.Zero
                ? QueryEndLocal.Date.AddDays(1).AddTicks(-1)
                : QueryEndLocal;
            var logs = await _queryOperationLogsUseCase.ExecuteAsync(
                new OperationLogQuery(
                    DateTime.SpecifyKind(QueryStartLocal, DateTimeKind.Local).ToUniversalTime(),
                    DateTime.SpecifyKind(endLocal, DateTimeKind.Local).ToUniversalTime(),
                    SelectedLevel,
                    CategoryFilter,
                    MaximumResults));
            DashboardViewModel.Replace(OperationLogs, logs.Select(log => new OperationLogRowModel
            {
                Timestamp = log.Timestamp.ToLocalTime(),
                Level = log.Level,
                Category = log.Category,
                Action = log.Action,
                Source = log.Source,
                Message = log.Message,
                Detail = log.Detail ?? ""
            }));
            QueryStatus = logs.Count == 0
                ? "No persisted operation logs match the filters."
                : $"Loaded {logs.Count} persisted operation logs.";
        }
        catch (Exception exception)
        {
            AppLogging.AppLogger.Error(exception, "Operation log query failed.");
            OperationLogs.Clear();
            QueryStatus = "Operation log query failed.";
        }
        finally
        {
            IsQuerying = false;
        }
    }

    [RelayCommand]
    private async Task SaveThresholdsAsync()
    {
        try
        {
            var configurations = Thresholds.Select(item => new TagRuntimeConfiguration(
                item.TagId,
                item.AlarmEnabled,
                item.WarningLow,
                item.AlarmLow,
                item.WarningHigh,
                item.AlarmHigh,
                item.IsHistorized,
                item.HistoryIntervalMs)).ToArray();
            await _saveTagRuntimeSettingsUseCase.ExecuteAsync(configurations);
            SettingsStatus = "Saved. Alarm and history settings take effect on the next frame.";
        }
        catch (Exception exception)
        {
            AppLogging.AppLogger.Error(exception, "Tag runtime settings save failed.");
            SettingsStatus = $"Save failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveRuntimeSettingsAsync()
    {
        try
        {
            var values = RuntimeSettings.ToDictionary(
                item => item.Name,
                item => double.Parse(item.Value, System.Globalization.CultureInfo.InvariantCulture),
                StringComparer.Ordinal);
            var current = _runtimeOptionsStore.Snapshot;
            var options = current with
            {
                DataGenerateInterval = TimeSpan.FromMilliseconds(values[RuntimeSettingKeys.DataGenerateIntervalMs]),
                DataSourceTimeoutPeriods = checked((int)values[RuntimeSettingKeys.DataSourceTimeoutPeriods]),
                UiRefreshInterval = TimeSpan.FromMilliseconds(values[RuntimeSettingKeys.UiRefreshIntervalMs]),
                HistoryBatchInterval = TimeSpan.FromMilliseconds(values[RuntimeSettingKeys.HistoryBatchIntervalMs]),
                HistoryRetentionDays = checked((int)values[RuntimeSettingKeys.HistoryRetentionDays]),
                AlarmBatchInterval = TimeSpan.FromMilliseconds(values[RuntimeSettingKeys.AlarmBatchIntervalMs]),
                OperationLogBatchInterval = TimeSpan.FromMilliseconds(values[RuntimeSettingKeys.OperationLogBatchIntervalMs]),
                TrendWindows = current.TrendWindows
                    .Where(window => window.TotalMinutes < values[RuntimeSettingKeys.MaximumTrendWindowMinutes])
                    .Append(TimeSpan.FromMinutes(values[RuntimeSettingKeys.MaximumTrendWindowMinutes]))
                    .Distinct()
                    .OrderBy(window => window)
                    .ToArray()
            };
            await _saveRuntimeSettingsUseCase.ExecuteAsync(options);
            SettingsStatus = "Saved. UI interval is immediate; acquisition interval applies next start; worker intervals apply next application start.";
        }
        catch (Exception exception)
        {
            AppLogging.AppLogger.Error(exception, "Runtime settings save failed.");
            SettingsStatus = $"Save failed: {exception.Message}";
        }
    }
}
