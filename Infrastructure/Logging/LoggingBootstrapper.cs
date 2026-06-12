using AppLogging;
using Serilog;

namespace Infrastructure.Logging;

public static class LoggingBootstrapper
{
    public static string DefaultLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MultiChannelMonitor",
        "logs");

    public static void Configure(string? logDirectory = null)
    {
        var resolvedLogDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? DefaultLogDirectory
            : logDirectory;

        Directory.CreateDirectory(resolvedLogDirectory);

        var logFilePath = Path.Combine(resolvedLogDirectory, "app-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.Async(sink => sink.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"))
            .CreateLogger();

        AppLogger.Configure(new SerilogAppLogger(Log.Logger));
        AppLogger.Info("Logging configured. LogDirectory={LogDirectory}", resolvedLogDirectory);
    }

    public static void Shutdown()
    {
        try
        {
            Log.CloseAndFlush();
        }
        finally
        {
            AppLogger.Reset();
        }
    }
}
