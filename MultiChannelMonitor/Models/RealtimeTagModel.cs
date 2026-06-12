using CommunityToolkit.Mvvm.ComponentModel;
using Domain.Tags;

namespace Presentation.Wpf.Models;

public sealed partial class RealtimeTagModel : ObservableObject
{
    [ObservableProperty]
    private string tagId = "";

    [ObservableProperty]
    private string displayName = "";

    [ObservableProperty]
    private string category = "";

    [ObservableProperty]
    private double value;

    [ObservableProperty]
    private string displayValue = "";

    [ObservableProperty]
    private string unit = "";

    [ObservableProperty]
    private TagQuality quality;

    [ObservableProperty]
    private TagAlarmState alarmState;

    [ObservableProperty]
    private DateTimeOffset timestamp;

    [ObservableProperty]
    private long sequenceNo;

    [ObservableProperty]
    private string dataStatus = "Live";
}
