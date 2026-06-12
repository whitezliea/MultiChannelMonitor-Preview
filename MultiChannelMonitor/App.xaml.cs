using AppLogging;
using Infrastructure.Logging;
using System.Windows;

namespace Presentation.Wpf;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        LoggingBootstrapper.Configure();
        AppLogger.Info("Application started.");

        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Fatal(args.Exception, "Unhandled dispatcher exception.");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                AppLogger.Fatal(exception, "Unhandled application domain exception.");
            }
        };

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            // 不使用 StartupUri：应用依赖异步初始化完成后，主窗口才获得完整且一致的对象图。
            var viewModel = await Bootstrap.ViewModelLocator.CreateShellViewModelAsync();
            var mainWindow = new MainWindow(viewModel);
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            AppLogger.Fatal(exception, "Application composition startup failed.");
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("Application exited.");
        LoggingBootstrapper.Shutdown();

        base.OnExit(e);
    }
}
