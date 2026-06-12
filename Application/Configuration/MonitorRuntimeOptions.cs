namespace Application.Configuration;

public sealed record MonitorRuntimeOptions
{
    public TimeSpan DataGenerateInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    public int DataSourceTimeoutPeriods { get; init; } = 3;
    public TimeSpan UiRefreshInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan HistoryBatchInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int HistoryMaxBatchSize { get; init; } = 100;
    public int HistoryRetentionDays { get; init; } = 30;
    public int HistoryRetentionDeleteBatchSize { get; init; } = 1000;
    public TimeSpan AlarmBatchInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int AlarmMaxBatchSize { get; init; } = 100;
    public TimeSpan OperationLogBatchInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int OperationLogMaxBatchSize { get; init; } = 100;
    public IReadOnlyList<TimeSpan> TrendWindows { get; init; } =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30)
    ];

    public TimeSpan MaximumTrendWindow => TrendWindows.Count == 0
        ? TimeSpan.Zero
        : TrendWindows.Max();

    public int TrendBufferCapacity => GetTrendPointCount(MaximumTrendWindow);

    public int GetTrendPointCount(TimeSpan window)
    {
        if (DataGenerateInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Data generation interval must be greater than zero.");
        }

        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Trend window must be greater than zero.");
        }

        return checked((int)Math.Ceiling(window.TotalMilliseconds / DataGenerateInterval.TotalMilliseconds));
    }

    public TimeSpan DataSourceTimeout => TimeSpan.FromTicks(
        checked(DataGenerateInterval.Ticks * DataSourceTimeoutPeriods));
}
