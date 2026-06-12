using Domain.Tags;

namespace Application.Abstractions.Persistence;

public interface IHistoryRepository
{
    Task AppendAsync(IReadOnlyCollection<TagValue> samples, CancellationToken cancellationToken);
    Task<HistoryQueryResult<TagValue>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Paged history query is not implemented by this repository.");

    Task<int> DeleteBeforeAsync(DateTime cutoffUtc, int maxRows, CancellationToken cancellationToken) =>
        throw new NotSupportedException("History retention is not implemented by this repository.");
}
