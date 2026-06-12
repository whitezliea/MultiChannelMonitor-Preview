using System.Collections.ObjectModel;
using Application.Abstractions.Persistence;
using Application.DTOs.Alarms;
using Application.UseCases.Alarms;
using AppLogging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Domain.Alarms;
using Presentation.Wpf.Models;

namespace Presentation.Wpf.ViewModels;

public sealed partial class AlarmCenterViewModel : PageViewModelBase, IDisposable
{
    private readonly AcknowledgeAlarmUseCase _acknowledgeAlarmUseCase;
    private readonly QueryAlarmsUseCase _queryAlarmsUseCase;
    private AlarmCenterFilter _selectedFilter = AlarmCenterFilter.Current;
    private AlarmCenterSnapshotDto _latestSnapshot = new([], [], []);
    private CancellationTokenSource? _queryCancellation;
    private bool _isUpdatingTimeSelection;

    public AlarmCenterViewModel(
        AcknowledgeAlarmUseCase acknowledgeAlarmUseCase,
        QueryAlarmsUseCase queryAlarmsUseCase) : base("Alarm Center")
    {
        _acknowledgeAlarmUseCase = acknowledgeAlarmUseCase;
        _queryAlarmsUseCase = queryAlarmsUseCase;
        var endTime = TruncateToMinute(DateTime.Now);
        SetTimeSelections(endTime.AddDays(-1), endTime);
    }

    public ObservableCollection<AlarmItemModel> AlarmEvents { get; } = [];
    public IReadOnlyList<AlarmLevel?> AvailableLevels { get; } = [null, .. Enum.GetValues<AlarmLevel>().Cast<AlarmLevel?>()];
    public IReadOnlyList<AlarmState?> AvailableStates { get; } = [null, .. Enum.GetValues<AlarmState>().Cast<AlarmState?>()];
    public IReadOnlyList<int> AvailableHours { get; } = Enumerable.Range(0, 24).ToArray();
    public IReadOnlyList<int> AvailableMinutes { get; } = Enumerable.Range(0, 60).ToArray();

    [ObservableProperty] private string selectedFilterText = "Current";
    [ObservableProperty] private DateTime? startDateLocal;
    [ObservableProperty] private int startHour;
    [ObservableProperty] private int startMinute;
    [ObservableProperty] private DateTime? endDateLocal;
    [ObservableProperty] private int endHour;
    [ObservableProperty] private int endMinute;
    [ObservableProperty] private string tagIdFilter = "";
    [ObservableProperty] private AlarmLevel? selectedLevel;
    [ObservableProperty] private AlarmState? selectedState;
    [ObservableProperty] private int currentPage = 1;
    [ObservableProperty] private int pageSize = 100;
    [ObservableProperty] private long totalCount;
    [ObservableProperty] private bool hasPreviousPage;
    [ObservableProperty] private bool hasNextPage;
    [ObservableProperty] private bool isQuerying;
    [ObservableProperty] private string queryStatus = "Current alarms are read from the runtime alarm index.";

    public DateTime? StartTimeLocal => ComposeLocalTime(StartDateLocal, StartHour, StartMinute);
    public DateTime? EndTimeLocal => ComposeLocalTime(EndDateLocal, EndHour, EndMinute);

    partial void OnStartDateLocalChanged(DateTime? value) => HandleTimeSelectionChanged(nameof(StartTimeLocal));

    partial void OnStartHourChanged(int value) => HandleTimeSelectionChanged(nameof(StartTimeLocal));

    partial void OnStartMinuteChanged(int value) => HandleTimeSelectionChanged(nameof(StartTimeLocal));

    partial void OnEndDateLocalChanged(DateTime? value) => HandleTimeSelectionChanged(nameof(EndTimeLocal));

    partial void OnEndHourChanged(int value) => HandleTimeSelectionChanged(nameof(EndTimeLocal));

    partial void OnEndMinuteChanged(int value) => HandleTimeSelectionChanged(nameof(EndTimeLocal));

    public void Refresh(AlarmCenterSnapshotDto snapshot)
    {
        _latestSnapshot = snapshot;
        if (_selectedFilter == AlarmCenterFilter.Current)
        {
            ReplaceRows(snapshot.CurrentAlarms);
            QueryStatus = $"{snapshot.CurrentAlarms.Count} current alarm(s).";
        }
    }

    [RelayCommand]
    private void ShowCurrent()
    {
        CancelQuery();
        _selectedFilter = AlarmCenterFilter.Current;
        SelectedFilterText = "Current";
        TotalCount = _latestSnapshot.CurrentAlarms.Count;
        HasPreviousPage = false;
        HasNextPage = false;
        ReplaceRows(_latestSnapshot.CurrentAlarms);
        QueryStatus = $"{TotalCount} current alarm(s).";
    }

    [RelayCommand]
    private async Task ShowRecentAsync()
    {
        _selectedFilter = AlarmCenterFilter.Recent;
        SelectedFilterText = "Recent (SQLite)";
        await RunQueryAsync(async token =>
        {
            var alarms = await _queryAlarmsUseCase.QueryRecentAsync(PageSize, token);
            TotalCount = alarms.Count;
            CurrentPage = 1;
            HasPreviousPage = false;
            HasNextPage = false;
            ReplaceRows(alarms);
            QueryStatus = $"Loaded {alarms.Count} recent persisted alarm(s).";
        });
    }

    [RelayCommand]
    private Task QueryHistoryAsync() => LoadHistoryPageAsync(1);

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private Task PreviousPageAsync() => LoadHistoryPageAsync(CurrentPage - 1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextPageAsync() => LoadHistoryPageAsync(CurrentPage + 1);

    [RelayCommand(CanExecute = nameof(IsQuerying))]
    private void CancelQuery() => _queryCancellation?.Cancel();

    [RelayCommand]
    private async Task AcknowledgeAsync(AlarmItemModel? alarm)
    {
        if (alarm is null || !alarm.CanAcknowledge) return;
        if (await _acknowledgeAlarmUseCase.ExecuteAsync(alarm.AlarmId))
        {
            alarm.State = AlarmState.Acknowledged;
            alarm.AcknowledgeTime = DateTime.Now;
            QueryStatus = "Alarm acknowledged; persistence remains active even when acquisition is stopped.";
        }
    }

    private Task LoadHistoryPageAsync(int page)
    {
        if (StartTimeLocal is not { } startTimeLocal
            || EndTimeLocal is not { } endTimeLocal)
        {
            QueryStatus = "Select both start and end dates.";
            return Task.CompletedTask;
        }

        if (startTimeLocal > endTimeLocal)
        {
            QueryStatus = "Start time must not be later than end time.";
            return Task.CompletedTask;
        }

        _selectedFilter = AlarmCenterFilter.History;
        SelectedFilterText = "History (SQLite)";
        return RunQueryAsync(async token =>
        {
            var query = new AlarmQuery(
                ToUtc(startTimeLocal),
                ToUtc(endTimeLocal),
                TagIdFilter,
                SelectedLevel,
                SelectedState,
                page,
                PageSize);
            var result = await _queryAlarmsUseCase.QueryHistoryAsync(query, token);
            CurrentPage = result.Page;
            TotalCount = result.TotalCount;
            HasPreviousPage = result.HasPreviousPage;
            HasNextPage = result.HasNextPage;
            ReplaceRows(result.Items);
            QueryStatus = result.TotalCount == 0
                ? "No persisted alarms match the filters."
                : $"Page {result.Page}: {result.Items.Count} of {result.TotalCount} alarm(s).";
        });
    }

    private async Task RunQueryAsync(Func<CancellationToken, Task> query)
    {
        CancelQuery();
        _queryCancellation?.Dispose();
        _queryCancellation = new CancellationTokenSource();
        IsQuerying = true;
        NotifyPagingCommands();
        try
        {
            QueryStatus = "Querying persisted alarms...";
            await query(_queryCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            QueryStatus = "Alarm query cancelled.";
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Alarm query failed.");
            QueryStatus = $"Alarm query failed: {exception.Message}";
        }
        finally
        {
            IsQuerying = false;
            NotifyPagingCommands();
        }
    }

    private void ReplaceRows(IEnumerable<AlarmEvent> alarms) =>
        DashboardViewModel.Replace(AlarmEvents, alarms.Select(DashboardViewModel.ToAlarmModel));

    private bool CanGoPrevious() => !IsQuerying && _selectedFilter == AlarmCenterFilter.History && HasPreviousPage;
    private bool CanGoNext() => !IsQuerying && _selectedFilter == AlarmCenterFilter.History && HasNextPage;

    private void NotifyPagingCommands()
    {
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        CancelQueryCommand.NotifyCanExecuteChanged();
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
        if (_isUpdatingTimeSelection || _selectedFilter != AlarmCenterFilter.History)
        {
            return;
        }

        CancelQuery();
        CurrentPage = 1;
        TotalCount = 0;
        HasPreviousPage = false;
        HasNextPage = false;
        AlarmEvents.Clear();
        QueryStatus = "History time range changed. Run the query again.";
        NotifyPagingCommands();
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
        CancelQuery();
        _queryCancellation?.Dispose();
    }

    private enum AlarmCenterFilter
    {
        Current,
        Recent,
        History
    }
}
