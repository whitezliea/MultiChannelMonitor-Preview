using CommunityToolkit.Mvvm.ComponentModel;
using Domain.Alarms;
using Domain.Tags;

namespace Presentation.Wpf.Models;

public sealed partial class AlarmItemModel : ObservableObject
{
    [ObservableProperty]
    private Guid alarmId;

    [ObservableProperty]
    private string tagId = "";

    [ObservableProperty]
    private AlarmLevel level;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAcknowledge))]
    private AlarmState state;

    [ObservableProperty]
    private TagAlarmState alarmType;

    [ObservableProperty]
    private double triggerValue;

    [ObservableProperty]
    private DateTime triggerTime;

    [ObservableProperty]
    private DateTime? acknowledgeTime;

    [ObservableProperty]
    private DateTime? recoverTime;

    [ObservableProperty]
    private DateTime? lastUpdatedTime;

    [ObservableProperty]
    private string closeReason = "";

    [ObservableProperty]
    private string message = "";

    public bool CanAcknowledge => State is AlarmState.Active;
}
