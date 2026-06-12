using AppLogging;
using Infrastructure.Logging;
using System.Windows;

namespace Presentation.Wpf;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("Application exited.");
        LoggingBootstrapper.Shutdown();

        base.OnExit(e);
    }
}
