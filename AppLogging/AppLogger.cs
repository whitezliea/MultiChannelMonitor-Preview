namespace AppLogging;

public static class AppLogger
{
    private static IAppLogger _current = NullAppLogger.Instance;

    public static void Configure(IAppLogger? logger)
    {
        _current = logger ?? NullAppLogger.Instance;
    }

    public static void Reset()
    {
        _current = NullAppLogger.Instance;
    }

    public static void Trace(string messageTemplate, params object?[] propertyValues)
    {
        _current.Trace(messageTemplate, propertyValues);
    }

    public static void Debug(string messageTemplate, params object?[] propertyValues)
    {
        _current.Debug(messageTemplate, propertyValues);
    }

    public static void Info(string messageTemplate, params object?[] propertyValues)
    {
        _current.Info(messageTemplate, propertyValues);
    }

    public static void Warn(string messageTemplate, params object?[] propertyValues)
    {
        _current.Warn(messageTemplate, propertyValues);
    }

    public static void Error(string messageTemplate, params object?[] propertyValues)
    {
        _current.Error(messageTemplate, propertyValues);
    }

    public static void Error(Exception exception, string messageTemplate, params object?[] propertyValues)
    {
        _current.Error(exception, messageTemplate, propertyValues);
    }

    public static void Fatal(Exception exception, string messageTemplate, params object?[] propertyValues)
    {
        _current.Fatal(exception, messageTemplate, propertyValues);
    }
}
