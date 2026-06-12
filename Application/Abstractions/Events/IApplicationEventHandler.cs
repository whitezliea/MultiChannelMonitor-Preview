namespace Application.Abstractions.Events;

public interface IApplicationEventHandler<in TEvent>
{
    ValueTask HandleAsync(TEvent applicationEvent, CancellationToken cancellationToken);
}
