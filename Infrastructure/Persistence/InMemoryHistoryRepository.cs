using Application.Abstractions.Persistence;
using Domain.Tags;

namespace Infrastructure.Persistence;

public sealed class InMemoryHistoryRepository : IHistoryRepository
{
    private readonly List<TagValue> _samples = [];
    private readonly object _syncRoot = new();

    public Task AppendAsync(IReadOnlyCollection<TagValue> samples, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            _samples.AddRange(samples);
        }
        return Task.CompletedTask;
    }

    public Task<HistoryQueryResult<TagValue>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        query.Validate();
        TagValue[] result;
        long totalCount;
        lock (_syncRoot)
        {
            var filtered = _samples
                .Where(sample => sample.TagId == query.TagId
                    && sample.Timestamp >= query.StartTimeUtc
                    && sample.Timestamp <= query.EndTimeUtc);
            var ordered = query.SortDirection == HistorySortDirection.Descending
                ? filtered.OrderByDescending(sample => sample.Timestamp)
                : filtered.OrderBy(sample => sample.Timestamp);
            totalCount = ordered.LongCount();
            result = ordered
                .Skip(checked((query.Page - 1) * query.PageSize))
                .Take(query.PageSize)
                .ToArray();
        }

        return Task.FromResult(new HistoryQueryResult<TagValue>(result, totalCount, query.Page, query.PageSize));
    }

    public async Task<IReadOnlyList<TagValue>> QueryAsync(
        string tagId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken) =>
        (await QueryAsync(
            new HistoryQuery(tagId, startTime, endTime, 1, HistoryQuery.MaximumPageSize),
            cancellationToken).ConfigureAwait(false)).Items;

    public Task<int> DeleteBeforeAsync(DateTime cutoffUtc, int maxRows, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Domain.Common.UtcDateTime.Require(cutoffUtc, nameof(cutoffUtc));
        if (maxRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRows));
        }

        lock (_syncRoot)
        {
            var samples = _samples
                .Where(sample => sample.Timestamp < cutoffUtc)
                .OrderBy(sample => sample.Timestamp)
                .Take(maxRows)
                .ToArray();
            foreach (var sample in samples)
            {
                _samples.Remove(sample);
            }

            return Task.FromResult(samples.Length);
        }
    }
}
