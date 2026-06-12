using System.Threading.Channels;

namespace Application.Queues;

public class AsyncItemQueue<T>
{
    private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();

    public ValueTask EnqueueAsync(T item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public ValueTask<T> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);

    public bool TryDequeue(out T item) => _channel.Reader.TryRead(out item!);
}
