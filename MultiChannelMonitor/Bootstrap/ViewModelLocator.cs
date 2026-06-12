using System.Windows.Threading;
using Presentation.Wpf.Services;
using Presentation.Wpf.ViewModels;

namespace Presentation.Wpf.Bootstrap;

public static class ViewModelLocator
{
    public static async Task<ShellViewModel> CreateShellViewModelAsync(
        CancellationToken cancellationToken = default)
    {
        // 先完成数据库、配置和告警恢复，再把可用的完整对象图交给 ViewModel。
        var composition = await RuntimeComposition
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(true);
        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        return new ShellViewModel(
            composition,
            new UiDispatcherService(dispatcher));
    }
}
