using Domain.Common;

namespace Application.Abstractions.Persistence;

public enum HistorySortDirection
{
    Ascending,
    Descending
}

public sealed record HistoryQuery(
    string TagId,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    int Page = 1,
    int PageSize = 200,
    HistorySortDirection SortDirection = HistorySortDirection.Ascending)
{
    public const int MaximumPageSize = 1000;
    public static readonly TimeSpan MaximumTimeRange = TimeSpan.FromDays(366);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TagId);
        UtcDateTime.Require(StartTimeUtc, nameof(StartTimeUtc));
        UtcDateTime.Require(EndTimeUtc, nameof(EndTimeUtc));
        if (StartTimeUtc > EndTimeUtc)
        {
            throw new ArgumentException("History query start time must not be later than end time.");
        }

        if (EndTimeUtc - StartTimeUtc > MaximumTimeRange)
        {
            throw new ArgumentException($"History query range must not exceed {MaximumTimeRange.TotalDays:0} days.");
        }

        if (Page <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Page), "Page must be greater than zero.");
        }

        if (PageSize is <= 0 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(PageSize), $"PageSize must be between 1 and {MaximumPageSize}.");
        }
    }
}

public sealed record HistoryQueryResult<T>(
    IReadOnlyList<T> Items,
    long TotalCount,
    int Page,
    int PageSize)
{
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => (long)Page * PageSize < TotalCount;
}

public sealed record HistoryRetentionResult(long DeletedCount, DateTime CutoffUtc);
