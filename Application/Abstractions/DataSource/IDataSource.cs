using Domain.Measurements;

namespace Application.Abstractions.DataSource;

public interface IDataSource
{
    IAsyncEnumerable<RawMeasurementFrame> ReadFramesAsync(CancellationToken cancellationToken);
}
