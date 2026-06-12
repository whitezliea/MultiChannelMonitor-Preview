using CommunityToolkit.Mvvm.ComponentModel;

namespace Presentation.Wpf.Models;

public sealed partial class RuntimeSettingModel : ObservableObject
{
    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string value = "";

    [ObservableProperty]
    private string unit = "";

    [ObservableProperty]
    private string effect = "";
}
