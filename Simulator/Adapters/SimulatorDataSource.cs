using Application.Abstractions.DataSource;
using Application.Abstractions.Time;
using Application.Configuration;
using AppLogging;
using Domain.Measurements;
using Simulator.Generators;

namespace Simulator.Adapters;

public sealed class SimulatorDataSource : IDataSource
{
    private readonly FakeDataGenerator _generator;
    private readonly IRuntimeOptionsStore _optionsStore;
    private readonly IClock _clock;

    public SimulatorDataSource(
        FakeDataGenerator generator,
        MonitorRuntimeOptions options,
        IClock clock)
        : this(generator, new RuntimeOptionsStore(options), clock)
    {
    }

    public SimulatorDataSource(
        FakeDataGenerator generator,
        IRuntimeOptionsStore optionsStore,
        IClock clock)
    {
        _generator = generator;
        _optionsStore = optionsStore;
        _clock = clock;
    }

    public async IAsyncEnumerable<RawMeasurementFrame> ReadFramesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var dataGenerateInterval = _optionsStore.Snapshot.DataGenerateInterval;
        while (!cancellationToken.IsCancellationRequested)
        {
            AppLogger.Info("SimulatorDataSurce | ReadFrameAsync");
            yield return _generator.NextFrame(_clock.UtcNow);
            try
            {
                await Task.Delay(dataGenerateInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                AppLogger.Error(ex.ToString());
                yield break;
            }
        }
    }
}
