namespace Application.Configuration;

public static class RuntimeSettingKeys
{
    public const string DataGenerateIntervalMs = "DataGenerateIntervalMs";
    public const string DataSourceTimeoutPeriods = "DataSourceTimeoutPeriods";
    public const string UiRefreshIntervalMs = "UiRefreshIntervalMs";
    public const string HistoryBatchIntervalMs = "HistoryBatchIntervalMs";
    public const string HistoryRetentionDays = "HistoryRetentionDays";
    public const string AlarmBatchIntervalMs = "AlarmBatchIntervalMs";
    public const string OperationLogBatchIntervalMs = "OperationLogBatchIntervalMs";
    public const string MaximumTrendWindowMinutes = "MaximumTrendWindowMinutes";
}

public enum SettingEffect
{
    Immediate,
    NextAcquisitionStart,
    NextApplicationStart
}

public sealed record RuntimeSettingsSaveResult(
    MonitorRuntimeOptions Options,
    IReadOnlyDictionary<string, SettingEffect> Effects);
