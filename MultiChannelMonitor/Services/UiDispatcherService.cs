using System.Windows.Threading;

namespace Presentation.Wpf.Services;

public sealed class UiDispatcherService
{
    private readonly Dispatcher _dispatcher;

    public UiDispatcherService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public Task InvokeAsync(Action action) => _dispatcher.InvokeAsync(action).Task;
}
