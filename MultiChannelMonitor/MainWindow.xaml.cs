using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using AppLogging;
using Presentation.Wpf.Bootstrap;
using Presentation.Wpf.ViewModels;

namespace Presentation.Wpf;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel = ViewModelLocator.CreateShellViewModel();
    private bool _shutdownStarted;
    private bool _shutdownCompleted;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Fatal(exception, "Application persistence startup failed.");
            Close();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_shutdownCompleted)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        base.OnClosing(e);
        e.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        IsEnabled = false;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() => _ = CompleteShutdownAsync()));
    }

    private async Task CompleteShutdownAsync()
    {
        try
        {
            await _viewModel.DisposeAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Application shutdown cleanup failed.");
        }
        finally
        {
            _shutdownCompleted = true;
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
            {
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal,
                    new Action(Close));
            }
        }
    }
}
