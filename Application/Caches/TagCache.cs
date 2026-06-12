using AppLogging;
using Domain.Common;
using Domain.Tags;

namespace Application.Caches;

public sealed class TagCache
{
    private readonly Dictionary<string, TagRuntimeState> _currentValues = [];
    private readonly Dictionary<string, Queue<TrendPoint>> _recentBuffers = [];
    private readonly object _syncRoot = new();

    public TagCache(int trendBufferCapacity)
    {
        if (trendBufferCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trendBufferCapacity));
        }

        TrendBufferCapacity = trendBufferCapacity;
    }

    public int TrendBufferCapacity { get; }

    public void Update(IEnumerable<TagRuntimeState> values)
    {
        AppLogger.Info("TagCache | Update");
        lock (_syncRoot)
        {
            foreach (var value in values)
            {
                UtcDateTime.Require(value.Timestamp, $"{nameof(values)}.{nameof(value.Timestamp)}");
                UtcDateTime.Require(value.LastUpdateTime, $"{nameof(values)}.{nameof(value.LastUpdateTime)}");
                _currentValues[value.TagId] = value;

                if (!value.NumericValue.HasValue)
                {
                    continue;
                }

                if (!_recentBuffers.TryGetValue(value.TagId, out var buffer))
                {
                    buffer = new Queue<TrendPoint>();
                    _recentBuffers[value.TagId] = buffer;
                }

                buffer.Enqueue(new TrendPoint(
                    value.Timestamp.UtcDateTime,
                    value.NumericValue.Value,
                    value.Quality));
                while (buffer.Count > TrendBufferCapacity)
                {
                    buffer.Dequeue();
                }
            }
        }
    }

    public TagSnapshot GetSnapshot()
    {
        AppLogger.Info("TagCache | GetSnapshot");
        lock (_syncRoot)
        {
            var values = _currentValues.Values
                .OrderBy(value => value.TagId, StringComparer.Ordinal)
                .ToArray();

            var buffers = _recentBuffers.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<TrendPoint>)pair.Value.ToArray(),
                StringComparer.Ordinal);

            return new TagSnapshot(values, buffers);
        }
    }
}
