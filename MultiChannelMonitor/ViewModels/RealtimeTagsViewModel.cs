using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Domain.Tags;
using Presentation.Wpf.Models;

namespace Presentation.Wpf.ViewModels;

public sealed partial class RealtimeTagsViewModel : PageViewModelBase
{
    private readonly IReadOnlyDictionary<string, TagDefinition> _definitions;
    private readonly Action<string> _openTrend;
    private List<TagRuntimeState> _latestTags = [];
    private string _dataStatus = "Stopped";

    public RealtimeTagsViewModel(
        IReadOnlyDictionary<string, TagDefinition> definitions,
        Action<string>? openTrend = null) : base("Realtime Tags")
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _openTrend = openTrend ?? (_ => { });
        Categories = ["All", .. definitions.Values.Select(item => item.Category.ToString()).Distinct().Order()];
        Qualities = ["All", .. Enum.GetNames<TagQuality>()];
        AlarmStates = ["All", .. Enum.GetNames<TagAlarmState>()];
    }

    public ObservableCollection<RealtimeTagModel> Tags { get; } = [];
    public IReadOnlyList<string> Categories { get; }
    public IReadOnlyList<string> Qualities { get; }
    public IReadOnlyList<string> AlarmStates { get; }

    [ObservableProperty]
    private RealtimeTagModel? selectedTag;

    [ObservableProperty]
    private string selectedCategory = "All";

    [ObservableProperty]
    private string selectedQuality = "All";

    [ObservableProperty]
    private string selectedAlarmState = "All";

    [ObservableProperty]
    private string searchText = "";

    public void Refresh(IReadOnlyList<TagRuntimeState> tags, string dataStatus = "Live")
    {
        _latestTags = tags.ToList();
        _dataStatus = dataStatus;
        ApplyFilter();
    }

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
    partial void OnSelectedQualityChanged(string value) => ApplyFilter();
    partial void OnSelectedAlarmStateChanged(string value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedTagChanged(RealtimeTagModel? value) =>
        OpenTrendCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void ClearFilter()
    {
        SelectedCategory = "All";
        SelectedQuality = "All";
        SelectedAlarmState = "All";
        SearchText = "";
    }

    [RelayCommand(CanExecute = nameof(CanOpenTrend))]
    private void OpenTrend()
    {
        if (SelectedTag is null || !CanOpenTrend())
        {
            return;
        }

        _openTrend(SelectedTag.TagId);
    }

    private void ApplyFilter()
    {
        var selectedTagId = SelectedTag?.TagId;
        var filtered = _latestTags.Where(MatchesFilter).Select(tag =>
        {
            var model = DashboardViewModel.ToTagModel(tag, _definitions);
            model.DataStatus = tag.Quality == TagQuality.Offline ? "Offline" : _dataStatus;
            return model;
        });
        DashboardViewModel.Replace(Tags, filtered);
        SelectedTag = selectedTagId is null
            ? Tags.FirstOrDefault()
            : Tags.FirstOrDefault(tag => tag.TagId == selectedTagId)
                ?? Tags.FirstOrDefault();
    }

    private bool MatchesFilter(TagRuntimeState tag)
    {
        _definitions.TryGetValue(tag.TagId, out var definition);
        if (SelectedCategory != "All" && definition?.Category.ToString() != SelectedCategory)
        {
            return false;
        }

        if (SelectedQuality != "All" && tag.Quality.ToString() != SelectedQuality)
        {
            return false;
        }

        if (SelectedAlarmState != "All" && tag.AlarmState.ToString() != SelectedAlarmState)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(SearchText)
            || tag.TagId.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || (definition?.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private bool CanOpenTrend()
    {
        if (SelectedTag is null
            || !_definitions.TryGetValue(SelectedTag.TagId, out var definition))
        {
            return false;
        }

        return definition.IsEnabled
            && definition.DataType is TagDataType.Double or TagDataType.Int or TagDataType.Number;
    }
}
