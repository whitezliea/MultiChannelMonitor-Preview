using System.Collections.ObjectModel;
using Application.Abstractions.Persistence;
using Application.Configuration;
using Application.DTOs.Charts;
using Application.Services.Trend;
using Application.UseCases.Export;
using Application.UseCases.History;
using AppLogging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Domain.Tags;
using Presentation.Wpf.Models;
using Presentation.Wpf.Services;

namespace Presentation.Wpf.ViewModels;

public sealed partial class HistoryViewModel : PageViewModelBase, IDisposable
{
    private readonly QueryHistorySamplesUseCase _queryUseCase;
    private readonly ExportHistoryCsvUseCase _exportUseCase;
    private readonly FilePickerService _filePickerService;
    private readonly IReadOnlyDictionary<string, TagDefinition> _definitions;
    private readonly ITagRuntimeConfigurationStore _configurationStore;
    private CancellationTokenSource? _operationCancellation;
    private HistoryQuery? _activeQuery;
    private bool _isUpdatingTimeSelection;

    public HistoryViewModel(
        IReadOnlyList<TagDefinition> definitions,
        QueryHistorySamplesUseCase queryUseCase,
        ExportHistoryCsvUseCase exportUseCase,
        FilePickerService filePickerService,
        ITagRuntimeConfigurationStore configurationStore) : base("History")
    {
        _queryUseCase = queryUseCase;
        _exportUseCase = exportUseCase;
        _filePickerService = filePickerService;
        _configurationStore = configurationStore;
        _definitions = definitions.ToDictionary(item => item.TagId, StringComparer.Ordinal);
        AvailableTags = definitions
            .Where(definition => definition.IsHistorized
                && definition.DataType is TagDataType.Double or TagDataType.Int or TagDataType.Number)
            .Select(item => item.TagId)
            .ToArray();
        SelectedTagId = AvailableTags.FirstOrDefault() ?? "";
        SetRange(TimeSpan.FromHours(24));
    }

    public IReadOnlyList<string> AvailableTags { get; }
    public IReadOnlyList<int> AvailableHours { get; } = Enumerable.Range(0, 24).ToArray();
    public IReadOnlyList<int> AvailableMinutes { get; } = Enumerable.Range(0, 60).ToArray();
    public ObservableCollection<HistorySampleRowModel> QueryResults { get; } = [];

    [ObservableProperty] private string selectedTagId;
    [ObservableProperty] private DateTime? startDateLocal;
    [ObservableProperty] private int startHour;
    [ObservableProperty] private int startMinute;
    [ObservableProperty] private DateTime? endDateLocal;
    [ObservableProperty] private int endHour;
    [ObservableProperty] private int endMinute;
    [ObservableProperty] private string querySummary = "Select a range and query SQLite history.";
    [ObservableProperty] private int currentPage = 1;
    [ObservableProperty] private int pageSize = 200;
    [ObservableProperty] private long totalCount;
    [ObservableProperty] private bool hasPreviousPage;
    [ObservableProperty] private bool hasNextPage;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private TrendSnapshotDto? currentTrendSnapshot;
    [ObservableProperty] private bool hasTrendPreview;

    public DateTime? StartTimeLocal => ComposeLocalTime(StartDateLocal, StartHour, StartMinute);
    public DateTime? EndTimeLocal => ComposeLocalTime(EndDateLocal, EndHour, EndMinute);

    partial void OnSelectedTagIdChanged(string value)
    {
        ResetQueryState();
    }

    partial void OnStartDateLocalChanged(DateTime? value) => HandleTimeSelectionChanged(nameof(StartTimeLocal));

    partial void OnStartHourChanged(int value) => HandleTimeSelectionChanged(nameof(StartTimeLocal));

    partial void OnStartMinuteChanged(int value) => HandleTimeSelectionChanged(nameof(StartTimeLocal));

    partial void OnEndDateLocalChanged(DateTime? value) => HandleTimeSelectionChanged(nameof(EndTimeLocal));

    partial void OnEndHourChanged(int value) => HandleTimeSelectionChanged(nameof(EndTimeLocal));

    partial void OnEndMinuteChanged(int value) => HandleTimeSelectionChanged(nameof(EndTimeLocal));

    [RelayCommand] private void LastHour() => SetRange(TimeSpan.FromHours(1));
    [RelayCommand] private void Last24Hours() => SetRange(TimeSpan.FromHours(24));
    [RelayCommand] private void Last7Days() => SetRange(TimeSpan.FromDays(7));

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private Task QueryAsync() => RunQueryAsync(page: 1);

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private Task PreviousPageAsync() => RunQueryAsync(CurrentPage - 1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextPageAsync() => RunQueryAsync(CurrentPage + 1);

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _operationCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (_activeQuery is null) return;
        var filePath = _filePickerService.PickCsvSavePath(
            $"history_{SelectedTagId.Replace('.', '_')}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        if (string.IsNullOrWhiteSpace(filePath)) return;

        BeginOperation();
        try
        {
            QuerySummary = "Exporting all matching history samples...";
            var count = await _exportUseCase.ExecuteAsync(
                _activeQuery with { Page = 1 },
                filePath,
                TimeZoneInfo.Local,
                _operationCancellation!.Token);
            QuerySummary = $"Exported {count} samples to {filePath}.";
        }
        catch (OperationCanceledException)
        {
            QuerySummary = "History export cancelled.";
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "History CSV export failed | TagId: {0}", SelectedTagId);
            QuerySummary = "History CSV export failed.";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task RunQueryAsync(int page)
    {
        if (string.IsNullOrWhiteSpace(SelectedTagId))
        {
            QuerySummary = "Select a historized numeric tag.";
            return;
        }

        if (StartTimeLocal is not { } startTimeLocal
            || EndTimeLocal is not { } endTimeLocal)
        {
            QuerySummary = "Select both start and end dates.";
            return;
        }

        if (startTimeLocal > endTimeLocal)
        {
            QuerySummary = "Start time must not be later than end time.";
            return;
        }

        BeginOperation();
        try
        {
            var query = new HistoryQuery(
                SelectedTagId,
                ToUtc(startTimeLocal),
                ToUtc(endTimeLocal),
                page,
                PageSize);
            var result = await _queryUseCase.ExecuteAsync(query, _operationCancellation!.Token);
            _activeQuery = query;
            DashboardViewModel.Replace(QueryResults, result.Items.Select(sample => new HistorySampleRowModel(
                sample.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"),
                sample.Value,
                sample.Quality,
                sample.AlarmState,
                sample.SequenceNo)));
            CurrentTrendSnapshot = BuildTrendSnapshot(query, result.Items);
            HasTrendPreview = result.Items.Any(sample => double.IsFinite(sample.Value));
            CurrentPage = result.Page;
            TotalCount = result.TotalCount;
            HasPreviousPage = result.HasPreviousPage;
            HasNextPage = result.HasNextPage;
            QuerySummary = result.TotalCount == 0
                ? "No persisted samples in the selected range."
                : $"Page {result.Page}: {result.Items.Count} of {result.TotalCount} samples.";
        }
        catch (OperationCanceledException)
        {
            QuerySummary = "History query cancelled.";
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Persistent history query failed | TagId: {0}", SelectedTagId);
            QuerySummary = "Persistent history query failed.";
        }
        finally
        {
            EndOperation();
        }
    }

    private bool CanStartOperation() => !IsBusy;
    private bool CanGoPrevious() => !IsBusy && HasPreviousPage;
    private bool CanGoNext() => !IsBusy && HasNextPage;
    private bool CanExport() => !IsBusy && _activeQuery is not null && TotalCount > 0;

    public void ApplyNavigationContext(string tagId, TimeSpan range)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
        if (!AvailableTags.Contains(tagId, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Tag is not available for history queries: {tagId}",
                nameof(tagId));
        }

        if (range <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(range),
                "History navigation range must be greater than zero.");
        }

        _operationCancellation?.Cancel();
        SelectedTagId = tagId;
        SetRange(range);
        QuerySummary = $"Ready to query {tagId} for the selected range.";
    }

    private void BeginOperation()
    {
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        NotifyCommandStates();
    }

    private void EndOperation()
    {
        IsBusy = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        QueryCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    private void SetRange(TimeSpan range)
    {
        var endTime = TruncateToMinute(DateTime.Now);
        SetTimeSelections(endTime - range, endTime);
        ResetQueryState();
    }

    private void SetTimeSelections(DateTime startTime, DateTime endTime)
    {
        _isUpdatingTimeSelection = true;
        try
        {
            StartDateLocal = startTime.Date;
            StartHour = startTime.Hour;
            StartMinute = startTime.Minute;
            EndDateLocal = endTime.Date;
            EndHour = endTime.Hour;
            EndMinute = endTime.Minute;
        }
        finally
        {
            _isUpdatingTimeSelection = false;
        }

        OnPropertyChanged(nameof(StartTimeLocal));
        OnPropertyChanged(nameof(EndTimeLocal));
    }

    private void HandleTimeSelectionChanged(string combinedPropertyName)
    {
        OnPropertyChanged(combinedPropertyName);
        if (!_isUpdatingTimeSelection)
        {
            ResetQueryState();
        }
    }

    private void ResetQueryState()
    {
        CurrentPage = 1;
        _activeQuery = null;
        TotalCount = 0;
        HasPreviousPage = false;
        HasNextPage = false;
        QueryResults.Clear();
        CurrentTrendSnapshot = null;
        HasTrendPreview = false;
        NotifyCommandStates();
    }

    private TrendSnapshotDto? BuildTrendSnapshot(
        HistoryQuery query,
        IReadOnlyList<TagValue> samples)
    {
        var finiteSamples = samples
            .Where(sample => double.IsFinite(sample.Value))
            .OrderBy(sample => sample.Timestamp)
            .ToArray();
        if (finiteSamples.Length == 0)
        {
            return null;
        }

        _definitions.TryGetValue(query.TagId, out var definition);
        _configurationStore.Snapshot.TryGetValue(query.TagId, out var configuration);
        var points = finiteSamples
            .Select(sample => new TrendPointDto(
                sample.Timestamp,
                sample.Value,
                sample.Quality))
            .ToArray();
        var series = new TrendSeriesDto(
            query.TagId,
            points,
            query.PageSize,
            SequenceNo: finiteSamples[^1].SequenceNo,
            SourceTimestamp: finiteSamples[^1].Timestamp);
        var window = query.EndTimeUtc - query.StartTimeUtc;
        if (window <= TimeSpan.Zero)
        {
            window = TimeSpan.FromSeconds(1);
        }

        return new TrendSnapshotDto(
            new TrendTagMetadataDto(
                query.TagId,
                definition?.DisplayName ?? query.TagId,
                definition?.Unit ?? "",
                definition?.MinValue,
                definition?.MaxValue,
                definition?.DataType ?? TagDataType.Number,
                definition is not null),
            window,
            series,
            BuildThresholds(configuration),
            TrendStatisticsCalculator.Calculate(points),
            new TrendDiagnosisDto(
                TrendDiagnosisState.NotEvaluated,
                false,
                false,
                "Historical diagnosis is not evaluated."),
            finiteSamples[^1].Value,
            finiteSamples[^1].Quality,
            finiteSamples[^1].AlarmState,
            finiteSamples[^1].Timestamp,
            configuration?.Revision ?? 0,
            query.EndTimeUtc);
    }

    private static IReadOnlyList<TrendThresholdDto> BuildThresholds(
        TagRuntimeConfiguration? configuration)
    {
        if (configuration is null || !configuration.AlarmEnabled)
        {
            return [];
        }

        var thresholds = new List<TrendThresholdDto>(4);
        AddThreshold(thresholds, "Alarm Low", configuration.AlarmLow, TrendThresholdType.AlarmLow);
        AddThreshold(thresholds, "Warning Low", configuration.WarningLow, TrendThresholdType.WarningLow);
        AddThreshold(thresholds, "Warning High", configuration.WarningHigh, TrendThresholdType.WarningHigh);
        AddThreshold(thresholds, "Alarm High", configuration.AlarmHigh, TrendThresholdType.AlarmHigh);
        return thresholds;
    }

    private static void AddThreshold(
        ICollection<TrendThresholdDto> thresholds,
        string name,
        double? value,
        TrendThresholdType type)
    {
        if (value.HasValue && double.IsFinite(value.Value))
        {
            thresholds.Add(new TrendThresholdDto(name, value.Value, type));
        }
    }

    private static DateTime ToUtc(DateTime localTime) =>
        localTime.Kind == DateTimeKind.Utc
            ? localTime
            : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), TimeZoneInfo.Local);

    private static DateTime? ComposeLocalTime(DateTime? date, int hour, int minute) =>
        date?.Date.AddHours(hour).AddMinutes(minute);

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind);

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
    }
}
