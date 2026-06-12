using Application.Abstractions.DataSource;
using AppLogging;
using Domain.Measurements;

namespace Application.Services;

public sealed class DataSourceService
{
    private readonly IDataSource _dataSource;

    public DataSourceService(IDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async IAsyncEnumerable<RawMeasurementFrame> ReadFramesAsync(
        [global::System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        AppLogger.Info("DataSourceService | ReadFrameAsync");
        await foreach (var frame in _dataSource.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
        {
            MeasurementTimeContract.Validate(frame);
            yield return frame;
        }
    }
}
