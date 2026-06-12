using Domain.Alarms;
using Domain.Common;

namespace Application.Abstractions.Persistence;

public enum AlarmSortDirection
{
    Ascending,
    Descending
}

public sealed record AlarmQuery(
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    string? TagId = null,
    AlarmLevel? Level = null,
    AlarmState? State = null,
    int Page = 1,
    int PageSize = 100,
    AlarmSortDirection SortDirection = AlarmSortDirection.Descending)
{
    public const int MaximumPageSize = 1000;
    public static readonly TimeSpan MaximumTimeRange = TimeSpan.FromDays(366);

    public void Validate()
    {
        UtcDateTime.Require(StartTimeUtc, nameof(StartTimeUtc));
        UtcDateTime.Require(EndTimeUtc, nameof(EndTimeUtc));
        if (StartTimeUtc > EndTimeUtc) throw new ArgumentException("Alarm query start time must not be later than end time.");
        if (EndTimeUtc - StartTimeUtc > MaximumTimeRange) throw new ArgumentException("Alarm query range must not exceed 366 days.");
        if (Page <= 0) throw new ArgumentOutOfRangeException(nameof(Page));
        if (PageSize is <= 0 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(PageSize));
    }
}

public sealed record AlarmQueryResult(
    IReadOnlyList<AlarmEvent> Items,
    long TotalCount,
    int Page,
    int PageSize)
{
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => (long)Page * PageSize < TotalCount;
}
