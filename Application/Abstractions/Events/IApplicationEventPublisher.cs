namespace Application.Abstractions.Events;

public interface IApplicationEventPublisher
{
    ValueTask PublishAsync<TEvent>(TEvent applicationEvent, CancellationToken cancellationToken);
}
