using AppLogging;
using Infrastructure.Logging;

namespace Tests.InfrastructureTests;

public sealed class LoggingBootstrapperTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(
        Path.GetTempPath(),
        "MultiChannelMonitorTests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        LoggingBootstrapper.Shutdown();

        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }

    [Fact]
    public void Configure_AllowsAppLoggerToWriteLocalFile()
    {
        LoggingBootstrapper.Configure(_logDirectory);

        AppLogger.Info("Integration log marker. RunId={RunId}", "logging-bootstrapper-test");
        LoggingBootstrapper.Shutdown();

        var logFile = Assert.Single(Directory.GetFiles(_logDirectory, "app-*.log"));
        var content = File.ReadAllText(logFile);

        Assert.Contains("Integration log marker.", content);
        Assert.Contains("logging-bootstrapper-test", content);
    }
}
