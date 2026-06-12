namespace AppLogging;

public interface IAppLogger
{
    void Trace(string messageTemplate, params object?[] propertyValues);

    void Debug(string messageTemplate, params object?[] propertyValues);

    void Info(string messageTemplate, params object?[] propertyValues);

    void Warn(string messageTemplate, params object?[] propertyValues);

    void Error(string messageTemplate, params object?[] propertyValues);

    void Error(Exception exception, string messageTemplate, params object?[] propertyValues);

    void Fatal(Exception exception, string messageTemplate, params object?[] propertyValues);
}
