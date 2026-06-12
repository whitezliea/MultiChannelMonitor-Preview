namespace Domain.Tasks;

public sealed record MeasurementTask(Guid Id, string TaskName, DateTime StartTime, MeasurementTaskStatus Status);
