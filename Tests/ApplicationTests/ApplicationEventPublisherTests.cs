using Application.Abstractions.Events;
using Application.Services;
using AppLogging;

namespace MultiChannelMonitor.Tests.ApplicationTests;

[Collection("AppLogger serial")]
public sealed class ApplicationEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_IsolatedHandlerFailure_ContinuesWithRemainingHandlers()
    {
        var publisher = new ApplicationEventPublisher();
        var recorder = new RecordingHandler<TestEvent>();
        publisher.Register(
            new ThrowingHandler<TestEvent>(),
            ApplicationEventHandlerFailurePolicy.Isolated);
        publisher.Register(recorder);

        await publisher.PublishAsync(new TestEvent(42), CancellationToken.None);

        var published = Assert.Single(recorder.Events);
        Assert.Equal(42, published.Value);
    }

    [Fact]
    public async Task PublishAsync_CriticalHandlerFailure_StopsDispatchAndRethrows()
    {
        var publisher = new ApplicationEventPublisher();
        var recorder = new RecordingHandler<TestEvent>();
        publisher.Register(new ThrowingHandler<TestEvent>());
        publisher.Register(recorder);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await publisher.PublishAsync(new TestEvent(42), CancellationToken.None));

        Assert.Equal("Expected test failure.", exception.Message);
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public async Task PublishAsync_HandlerFailure_LogsEventHandlerAndPolicy()
    {
        var logger = new RecordingAppLogger();
        AppLogger.Configure(logger);
        try
        {
            var publisher = new ApplicationEventPublisher();
            publisher.Register(
                new ThrowingHandler<TestEvent>(),
                ApplicationEventHandlerFailurePolicy.Isolated);

            await publisher.PublishAsync(new TestEvent(42), CancellationToken.None);

            var error = Assert.Single(logger.Errors);
            Assert.IsType<InvalidOperationException>(error.Exception);
            Assert.Equal(typeof(TestEvent).FullName, error.PropertyValues[0]);
            Assert.Equal(typeof(ThrowingHandler<TestEvent>).FullName, error.PropertyValues[1]);
            Assert.Equal(ApplicationEventHandlerFailurePolicy.Isolated, error.PropertyValues[2]);
        }
        finally
        {
            AppLogger.Reset();
        }
    }

    [Fact]
    public async Task PublishAsync_CriticalFailure_ChangesRuntimeLifecycleToFaulted()
    {
        var publisher = new ApplicationEventPublisher();
        publisher.Register(new ThrowingHandler<TestEvent>());
        await using var lifecycle = new RuntimeLifecycleCoordinator(cancellationToken =>
            publisher.PublishAsync(new TestEvent(42), cancellationToken).AsTask());
        var faulted = new TaskCompletionSource<RuntimeLifecycleStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lifecycle.StatusChanged += (_, status) =>
        {
            if (status.State == RuntimeLifecycleState.Faulted)
            {
                faulted.TrySetResult(status);
            }
        };

        Assert.True(await lifecycle.StartAsync());
        var status = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<InvalidOperationException>(status.Error);
        Assert.Equal(RuntimeLifecycleState.Faulted, lifecycle.Status.State);
    }

    [Fact]
    public async Task PublishAsync_CancellationRequested_PropagatesForIsolatedHandler()
    {
        var publisher = new ApplicationEventPublisher();
        using var cancellation = new CancellationTokenSource();
        publisher.Register(
            new CancelingHandler<TestEvent>(cancellation),
            ApplicationEventHandlerFailurePolicy.Isolated);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await publisher.PublishAsync(new TestEvent(42), cancellation.Token));
    }

    private sealed record TestEvent(int Value);

    private sealed class RecordingHandler<TEvent> : IApplicationEventHandler<TEvent>
    {
        public List<TEvent> Events { get; } = [];

        public ValueTask HandleAsync(TEvent applicationEvent, CancellationToken cancellationToken)
        {
            Events.Add(applicationEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingHandler<TEvent> : IApplicationEventHandler<TEvent>
    {
        public ValueTask HandleAsync(TEvent applicationEvent, CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("Expected test failure."));
    }

    private sealed class CancelingHandler<TEvent>(CancellationTokenSource cancellation)
        : IApplicationEventHandler<TEvent>
    {
        public ValueTask HandleAsync(TEvent applicationEvent, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return ValueTask.FromCanceled(cancellationToken);
        }
    }

    private sealed class RecordingAppLogger : IAppLogger
    {
        public List<ErrorEntry> Errors { get; } = [];

        public void Trace(string messageTemplate, params object?[] propertyValues) { }
        public void Debug(string messageTemplate, params object?[] propertyValues) { }
        public void Info(string messageTemplate, params object?[] propertyValues) { }
        public void Warn(string messageTemplate, params object?[] propertyValues) { }
        public void Error(string messageTemplate, params object?[] propertyValues) { }

        public void Error(
            Exception exception,
            string messageTemplate,
            params object?[] propertyValues) =>
            Errors.Add(new ErrorEntry(exception, messageTemplate, propertyValues));

        public void Fatal(Exception exception, string messageTemplate, params object?[] propertyValues) { }
    }

    private sealed record ErrorEntry(
        Exception Exception,
        string MessageTemplate,
        object?[] PropertyValues);
}
