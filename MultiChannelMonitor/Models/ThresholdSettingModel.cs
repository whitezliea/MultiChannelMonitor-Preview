using CommunityToolkit.Mvvm.ComponentModel;

namespace Presentation.Wpf.Models;

public sealed partial class ThresholdSettingModel : ObservableObject
{
    [ObservableProperty]
    private string tagId = "";

    [ObservableProperty]
    private double? warningLow;

    [ObservableProperty]
    private double? alarmLow;

    [ObservableProperty]
    private double? warningHigh;

    [ObservableProperty]
    private double? alarmHigh;

    [ObservableProperty]
    private bool alarmEnabled;

    [ObservableProperty]
    private bool isHistorized;

    [ObservableProperty]
    private int historyIntervalMs;
}
