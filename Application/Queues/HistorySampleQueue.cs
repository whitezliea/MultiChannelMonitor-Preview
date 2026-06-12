using Domain.Tags;

namespace Application.Queues;

public sealed class HistorySampleQueue : AsyncItemQueue<TagValue>;
