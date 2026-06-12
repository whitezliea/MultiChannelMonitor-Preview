using CommunityToolkit.Mvvm.ComponentModel;

namespace Presentation.Wpf.Models;

public sealed partial class TrendPointModel : ObservableObject
{
    [ObservableProperty]
    private string timeText = "";

    [ObservableProperty]
    private double value;
}
