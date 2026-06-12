using Application.Services;

namespace MultiChannelMonitor.Tests.ApplicationTests;

public sealed class RuntimeLifecycleCoordinatorTests
{
    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_DoesNotStartSecondRuntime()
    {
        var started = NewSignal();
        var startCount = 0;
        await using var coordinator = new RuntimeLifecycleCoordinator(async cancellationToken =>
        {
            Interlocked.Increment(ref startCount);
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });

        Assert.True(await coordinator.StartAsync());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(await coordinator.StartAsync());
        Assert.Equal(1, Volatile.Read(ref startCount));
        Assert.Equal(RuntimeLifecycleState.Running, coordinator.Status.State);

        Assert.True(await coordinator.StopAsync());
        Assert.Equal(RuntimeLifecycleState.Stopped, coordinator.Status.State);
    }

    [Fact]
    public async Task StopAsync_WaitsForRuntimeCleanupBeforeCompleting()
    {
        var cancellationObserved = NewSignal();
        var allowCleanup = NewSignal();
        await using var coordinator = new RuntimeLifecycleCoordinator(async cancellationToken =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellationObserved.TrySetResult();
                await allowCleanup.Task;
            }
        });

        Assert.True(await coordinator.StartAsync());
        var stopTask = coordinator.StopAsync();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(stopTask.IsCompleted);
        Assert.Equal(RuntimeLifecycleState.Stopping, coordinator.Status.State);

        allowCleanup.TrySetResult();
        Assert.True(await stopTask);
        Assert.Equal(RuntimeLifecycleState.Stopped, coordinator.Status.State);
    }

    [Fact]
    public async Task StopThenStart_DoesNotOverlapRuntimeInstances()
    {
        var startCount = 0;
        var activeCount = 0;
        var maximumActiveCount = 0;
        await using var coordinator = new RuntimeLifecycleCoordinator(async cancellationToken =>
        {
            Interlocked.Increment(ref startCount);
            var active = Interlocked.Increment(ref activeCount);
            UpdateMaximum(ref maximumActiveCount, active);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref activeCount);
            }
        });

        Assert.True(await coordinator.StartAsync());
        Assert.True(await coordinator.StopAsync());
        Assert.True(await coordinator.StartAsync());
        Assert.True(await coordinator.StopAsync());

        Assert.Equal(2, Volatile.Read(ref startCount));
        Assert.Equal(1, Volatile.Read(ref maximumActiveCount));
        Assert.Equal(0, Volatile.Read(ref activeCount));
    }

    [Fact]
    public async Task RuntimeFailure_ChangesStatusToFaultedAndKeepsError()
    {
        var faulted = new TaskCompletionSource<RuntimeLifecycleStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new RuntimeLifecycleCoordinator(
            _ => Task.FromException(new InvalidOperationException("runtime failed")));
        coordinator.StatusChanged += (_, status) =>
        {
            if (status.State == RuntimeLifecycleState.Faulted)
            {
                faulted.TrySetResult(status);
            }
        };

        Assert.True(await coordinator.StartAsync());
        var status = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(RuntimeLifecycleState.Faulted, coordinator.Status.State);
        Assert.IsType<InvalidOperationException>(status.Error);
        Assert.Equal("runtime failed", status.Error.Message);
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        var current = Volatile.Read(ref maximum);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref maximum, candidate, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}
