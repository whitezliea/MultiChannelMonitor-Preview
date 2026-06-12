using AppLogging;

namespace Tests.AppLoggingTests;

[Collection("AppLogger serial")]
public sealed class AppLoggerTests
{
    [Fact]
    public void Info_ForwardsMessageTemplateToConfiguredLogger()
    {
        var logger = new RecordingAppLogger();

        AppLogger.Configure(logger);
        AppLogger.Info("Test message. Value={Value}", 42);

        try
        {
            Assert.Contains(logger.Entries, entry =>
                entry.Level == AppLogLevel.Info &&
                entry.MessageTemplate == "Test message. Value={Value}" &&
                entry.PropertyValues.Length == 1 &&
                (int)entry.PropertyValues[0]! == 42);
        }
        finally
        {
            AppLogger.Reset();
        }
    }

    [Fact]
    public void CallsBeforeConfigure_DoNotThrow()
    {
        AppLogger.Reset();

        AppLogger.Debug("Debug before configure.");
        AppLogger.Error(new InvalidOperationException("test"), "Error before configure.");
    }

    private sealed class RecordingAppLogger : IAppLogger
    {
        public List<LogEntry> Entries { get; } = [];

        public void Trace(string messageTemplate, params object?[] propertyValues)
        {
            Entries.Add(new LogEntry(AppLogLevel.Trace, messageTemplate, propertyValues));
        }

        public void Debug(string messageTemplate, params object?[] propertyValues)
        {
            Entries.Add(new LogEntry(AppLogLevel.Debug, messageTemplate, propertyValues));
        }

        public void Info(string messageTemplate, params object?[] propertyValues)
        {
            Entries.Add(new LogEntry(AppLogLevel.Info, messageTemplate, propertyValues));
        }

        public void Warn(string messageTemplate, params object?[] propertyValues)
        {
            Entries.Add(new LogEntry(AppLogLevel.Warn, messageTemplate, propertyValues));
        }

        public void Error(string messageTemplate, params object?[] propertyValues)
        {
            Entries.Add(new LogEntry(AppLogLevel.Error, messageTemplate, propertyValues));
        }

        public void Error(Exception exception, string messageTemplate, params object?[] propertyValues)
        {
            Entries.Add(new LogEntry(AppLogLevel.Error, messageTemplate, propertyValues));
        }

        public void Fatal(Exception exception, string messageTemplate, params object?[] propertyValues)
        {
            Entries.Add(new LogEntry(AppLogLevel.Fatal, messageTemplate, propertyValues));
        }
    }

    private sealed record LogEntry(
        AppLogLevel Level,
        string MessageTemplate,
        object?[] PropertyValues);
}
