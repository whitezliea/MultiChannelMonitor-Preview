namespace Domain.Common;

public sealed record TimeRange(DateTime StartTime, DateTime EndTime)
{
    public bool Contains(DateTime timestamp) => timestamp >= StartTime && timestamp <= EndTime;
}
