namespace Domain.Logs;

public sealed record OperationLog(
    DateTime Timestamp,
    OperationLogLevel Level,
    string Category,
    string Message,
    string Action = "",
    string Source = "",
    string? Detail = null,
    string? CorrelationId = null,
    long Id = 0);

public sealed record OperationLogQuery(
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    OperationLogLevel? Level = null,
    string? Category = null,
    int MaxCount = 200);

public enum OperationLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}
