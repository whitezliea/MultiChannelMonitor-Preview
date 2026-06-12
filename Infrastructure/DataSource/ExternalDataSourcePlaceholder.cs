using Application.Abstractions.DataSource;
using Domain.Measurements;

namespace Infrastructure.DataSource;

public sealed class ExternalDataSourcePlaceholder : IDataSource
{
    public async IAsyncEnumerable<RawMeasurementFrame> ReadFramesAsync([global::System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }
}
