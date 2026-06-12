using CommunityToolkit.Mvvm.ComponentModel;
using Domain.Logs;

namespace Presentation.Wpf.Models;

public sealed partial class OperationLogRowModel : ObservableObject
{
    [ObservableProperty]
    private DateTime timestamp;

    [ObservableProperty]
    private OperationLogLevel level;

    [ObservableProperty]
    private string category = "";

    [ObservableProperty]
    private string action = "";

    [ObservableProperty]
    private string source = "";

    [ObservableProperty]
    private string message = "";

    [ObservableProperty]
    private string detail = "";
}
