using Application.Abstractions.Events;
using AppLogging;

namespace Application.Services;

public sealed class ApplicationEventPublisher : IApplicationEventPublisher
{
    private readonly Dictionary<Type, List<HandlerRegistration>> _handlers = [];

    public void Register<TEvent>(
        IApplicationEventHandler<TEvent> handler,
        ApplicationEventHandlerFailurePolicy failurePolicy = ApplicationEventHandlerFailurePolicy.Critical)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!Enum.IsDefined(failurePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(failurePolicy), failurePolicy, "Unsupported failure policy.");
        }

        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
        {
            handlers = [];
            _handlers[typeof(TEvent)] = handlers;
        }

        handlers.Add(new HandlerRegistration(
            handler.GetType(),
            failurePolicy,
            (applicationEvent, cancellationToken) =>
                handler.HandleAsync((TEvent)applicationEvent, cancellationToken)));
    }

    public async ValueTask PublishAsync<TEvent>(TEvent applicationEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
        {
            return;
        }

        foreach (var registration in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await registration.Handler(applicationEvent!, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                AppLogger.Error(
                    exception,
                    "Application event handler failed | EventType: {0} | HandlerType: {1} | FailurePolicy: {2}",
                    typeof(TEvent).FullName ?? typeof(TEvent).Name,
                    registration.HandlerType.FullName ?? registration.HandlerType.Name,
                    registration.FailurePolicy);

                if (registration.FailurePolicy == ApplicationEventHandlerFailurePolicy.Critical)
                {
                    throw;
                }
            }
        }
    }

    private sealed record HandlerRegistration(
        Type HandlerType,
        ApplicationEventHandlerFailurePolicy FailurePolicy,
        Func<object, CancellationToken, ValueTask> Handler);
}
