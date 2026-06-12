using CommunityToolkit.Mvvm.ComponentModel;

namespace Presentation.Wpf.Models;

public sealed partial class MetricCardModel : ObservableObject
{
    [ObservableProperty]
    private string title = "";

    [ObservableProperty]
    private string value = "";

    [ObservableProperty]
    private string subtitle = "";
}
