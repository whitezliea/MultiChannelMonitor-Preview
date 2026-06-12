namespace AppLogging;

public sealed class NullAppLogger : IAppLogger
{
    public static readonly NullAppLogger Instance = new();

    private NullAppLogger()
    {
    }

    public void Trace(string messageTemplate, params object?[] propertyValues)
    {
    }

    public void Debug(string messageTemplate, params object?[] propertyValues)
    {
    }

    public void Info(string messageTemplate, params object?[] propertyValues)
    {
    }

    public void Warn(string messageTemplate, params object?[] propertyValues)
    {
    }

    public void Error(string messageTemplate, params object?[] propertyValues)
    {
    }

    public void Error(Exception exception, string messageTemplate, params object?[] propertyValues)
    {
    }

    public void Fatal(Exception exception, string messageTemplate, params object?[] propertyValues)
    {
    }
}
