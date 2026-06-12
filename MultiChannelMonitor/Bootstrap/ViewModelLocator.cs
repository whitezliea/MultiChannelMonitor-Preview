using System.Windows.Threading;
using Presentation.Wpf.Services;
using Presentation.Wpf.ViewModels;

namespace Presentation.Wpf.Bootstrap;

public static class ViewModelLocator
{
    public static ShellViewModel CreateShellViewModel()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        return new ShellViewModel(
            new RuntimeComposition(),
            new UiDispatcherService(dispatcher));
    }
}
