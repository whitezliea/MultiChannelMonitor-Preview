namespace Domain.Tasks;

public sealed record MeasurementTaskSummary(int TotalCount, int RunningCount, int CompletedCount, int FailedCount);
